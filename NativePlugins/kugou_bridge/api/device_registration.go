package api

import (
	"bytes"
	"crypto/aes"
	"crypto/cipher"
	"crypto/rand"
	"crypto/rsa"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"io"
	"net/http"
	"net/url"
	"strconv"
	"strings"
	"time"
)

func (c *Client) registerDeviceV2(device Device) (map[string]interface{}, error) {
	session, _ := c.session(0)
	payload := map[string]interface{}{
		"availableRamSize": int64(4983533568), "availableRomSize": int64(48114719), "availableSDSize": int64(48114717),
		"basebandVer": "", "batteryLevel": 100, "batteryStatus": 3,
		"brand": "Redmi", "buildSerial": "unknown", "device": "marble",
		"imei": device.UUID, "imsi": "", "manufacturer": "Xiaomi", "uuid": device.UUID,
		"accelerometer": false, "accelerometerValue": "", "gravity": false, "gravityValue": "",
		"gyroscope": false, "gyroscopeValue": "", "light": false, "lightValue": "",
		"magnetic": false, "magneticValue": "", "orientation": false, "orientationValue": "",
		"pressure": false, "pressureValue": "", "step_counter": false, "step_counterValue": "",
		"temperature": false, "temperatureValue": "",
	}
	seed, encryptedBody, err := encryptRegistrationPayload(payload)
	if err != nil {
		return nil, &BridgeError{Code: ErrorDecode, Message: "device request encryption failed", Operation: "registerDevice", Cause: err.Error()}
	}
	keyPayload, err := json.Marshal(struct {
		AES   string `json:"aes"`
		UID   int64  `json:"uid"`
		Token string `json:"token"`
	}{seed, session.UserID, session.Token})
	if err != nil {
		return nil, &BridgeError{Code: ErrorDecode, Message: "device key encoding failed", Operation: "registerDevice", Cause: err.Error()}
	}
	publicKey, err := parsePublicKey(liteVerifyPublicKey)
	if err != nil {
		return nil, &BridgeError{Code: ErrorDecode, Message: "device public key is invalid", Operation: "registerDevice", Cause: err.Error()}
	}
	encryptedKey, err := rsa.EncryptPKCS1v15(rand.Reader, publicKey, keyPayload)
	if err != nil {
		return nil, &BridgeError{Code: ErrorDecode, Message: "device key encryption failed", Operation: "registerDevice", Cause: err.Error()}
	}
	clientTime := strconv.FormatInt(time.Now().Unix(), 10)
	params := map[string]string{
		"dfid": device.DFID, "mid": device.MID, "uuid": "-", "appid": strconv.Itoa(appID),
		"clientver": strconv.Itoa(clientVer), "userid": strconv.FormatInt(session.UserID, 10),
		"clienttime": clientTime, "part": "1", "platid": "1", "p": hex.EncodeToString(encryptedKey),
	}
	if session.Token != "" {
		params["token"] = session.Token
	}
	params["signature"] = signature(params, encryptedBody, androidSecret)
	endpoint, err := url.Parse("https://userservice.kugou.com/risk/v2/r_register_dev")
	if err != nil {
		return nil, &BridgeError{Code: ErrorInvalidArgument, Message: "invalid endpoint", Operation: "registerDevice", Cause: err.Error()}
	}
	query := endpoint.Query()
	for key, value := range params {
		query.Set(key, value)
	}
	endpoint.RawQuery = query.Encode()
	request, err := http.NewRequest(http.MethodPost, endpoint.String(), strings.NewReader(encryptedBody))
	if err != nil {
		return nil, &BridgeError{Code: ErrorNetwork, Message: "request creation failed", Operation: "registerDevice", Cause: err.Error()}
	}
	request.Header.Set("User-Agent", "Android15-1070-11040-201-0-LOGIN-wifi")
	request.Header.Set("dfid", device.DFID)
	request.Header.Set("mid", device.MID)
	request.Header.Set("clienttime", clientTime)
	setCommonHeaders(request.Header)
	response, err := c.httpClient.Do(request)
	if err != nil {
		return nil, &BridgeError{Code: ErrorNetwork, Message: "request failed", Operation: "registerDevice", Cause: err.Error()}
	}
	defer response.Body.Close()
	responseBody, err := io.ReadAll(io.LimitReader(response.Body, 16<<20))
	if err != nil {
		return nil, &BridgeError{Code: ErrorNetwork, Message: "response read failed", Operation: "registerDevice", Cause: err.Error()}
	}
	if response.StatusCode < 200 || response.StatusCode >= 300 {
		return nil, &BridgeError{Code: ErrorHTTP, Message: "unexpected HTTP status", Operation: "registerDevice", HTTPStatus: response.StatusCode, Cause: truncate(string(responseBody), 512)}
	}
	plain, err := decryptRegistrationPayload(responseBody, seed)
	if err != nil {
		return nil, &BridgeError{Code: ErrorDecode, Message: "device response decryption failed", Operation: "registerDevice", Cause: err.Error()}
	}
	var decoded map[string]interface{}
	if err := json.Unmarshal(plain, &decoded); err != nil {
		return nil, &BridgeError{Code: ErrorDecode, Message: "invalid device response", Operation: "registerDevice", Cause: err.Error()}
	}
	if numberValue(decoded["status"]) == 0 {
		return nil, &BridgeError{Code: ErrorAPI, Message: firstString(decoded, "error", "msg", "message"), Operation: "registerDevice", APIErrorCode: int(numberValue(decoded["error_code"])), Cause: truncate(string(plain), 512)}
	}
	return decoded, nil
}

func encryptRegistrationPayload(value interface{}) (string, string, error) {
	plain, err := json.Marshal(value)
	if err != nil {
		return "", "", err
	}
	seed := randomHex(3)
	key, iv := registrationKey(seed)
	block, err := aes.NewCipher(key)
	if err != nil {
		return "", "", err
	}
	padded := pkcs7Pad(plain, block.BlockSize())
	encrypted := make([]byte, len(padded))
	cipher.NewCBCEncrypter(block, iv).CryptBlocks(encrypted, padded)
	return seed, base64.StdEncoding.EncodeToString(encrypted), nil
}

func decryptRegistrationPayload(value []byte, seed string) ([]byte, error) {
	key, iv := registrationKey(seed)
	block, err := aes.NewCipher(key)
	if err != nil {
		return nil, err
	}
	if len(value) == 0 || len(value)%block.BlockSize() != 0 {
		return nil, io.ErrUnexpectedEOF
	}
	decrypted := make([]byte, len(value))
	cipher.NewCBCDecrypter(block, iv).CryptBlocks(decrypted, value)
	padding := int(decrypted[len(decrypted)-1])
	if padding < 1 || padding > block.BlockSize() || !bytes.Equal(decrypted[len(decrypted)-padding:], bytes.Repeat([]byte{byte(padding)}, padding)) {
		return nil, io.ErrUnexpectedEOF
	}
	return decrypted[:len(decrypted)-padding], nil
}

func registrationKey(seed string) ([]byte, []byte) {
	digest := md5Hex(seed)
	return []byte(digest[:16]), []byte(digest[16:])
}
