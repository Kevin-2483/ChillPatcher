package api

import (
	"crypto/aes"
	"crypto/cipher"
	"crypto/md5"
	"crypto/rand"
	"crypto/rsa"
	"crypto/sha256"
	"crypto/x509"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"encoding/pem"
	"fmt"
	"io"
	"math"
	"math/big"
	"math/bits"
	"net"
	"net/http"
	"strings"
	"time"
)

const sidPublicKey = `-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAoW2+Ylo8ALePSQTP0xBF
lFmEOHvBD9tS+s7DBlfKEu3RzzvZTaX1JtYbX4+AVUqj6ARz8IM+CKByqGFvbHN/
W64XxNI+q7z36ajCL3VTJ2W5G9MCJitc6oGbire4NQfhaEq0nC+hxBWQvCbIFflA
2ItrLUbSU7z1bHA/a+jlQm4OWvY+IKnTryOJTPuT1yNOVjbJ8wBLKy2DgQr9pPqW
PmEQtGpR5IM9V8Kao6PaSdKYOWGbX3i2+RzIKhvZUxxtJwdVbqPlDPlW9h4/xIBc
56Lgvr4aIl8nFtwbj4UJVUTFuGrs0tY9H/tXvZ22dUCKuGxW/gW7ZF+gXz6vHtYa
rQIDAQAB
-----END PUBLIC KEY-----`

const liteVerifyPublicKey = `-----BEGIN PUBLIC KEY-----
MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQDECi0Np2UR87scwrvTr72L6oO01rBbbBPriSDFPxr3Z5syug0O24QyQO8bg27+0+4kBzTBTBOZ/WWU0WryL1JSXRTXLgFVxtzIY41Pe7lPOgsfTCn5kZcvKhYKJesKnnJDNr5/abvTGf+rHG3YRwsCHcQ08/q6ifSioBszvb3QiwIDAQAB
-----END PUBLIC KEY-----`

type verificationSubmit struct {
	EventID    string `json:"eventid"`
	VType      int    `json:"vType"`
	VerifyCode string `json:"verifycode"`
}

func (c *Client) startVerificationServer() error {
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		return &BridgeError{Code: ErrorNetwork, Message: "verification server failed to start", Operation: "verification", Cause: err.Error()}
	}
	mux := http.NewServeMux()
	mux.HandleFunc("/verify", c.serveVerificationPage)
	mux.HandleFunc("/api/info", c.serveVerificationInfo)
	mux.HandleFunc("/api/submit", c.serveVerificationSubmit)
	c.verificationURL = "http://" + listener.Addr().String()
	go func() { _ = http.Serve(listener, mux) }()
	return nil
}

func (c *Client) serveVerificationPage(writer http.ResponseWriter, request *http.Request) {
	writer.Header().Set("Content-Type", "text/html; charset=utf-8")
	_, _ = io.WriteString(writer, verificationHTML)
}

func (c *Client) serveVerificationInfo(writer http.ResponseWriter, request *http.Request) {
	if !isLoopbackRequest(request) {
		writeVerificationJSON(writer, http.StatusForbidden, map[string]interface{}{"status": 0, "message": "仅允许本机验证"})
		return
	}
	eventID := strings.TrimSpace(request.URL.Query().Get("eventid"))
	if eventID == "" {
		writeVerificationJSON(writer, http.StatusBadRequest, map[string]interface{}{"status": 0, "message": "缺少验证事件"})
		return
	}
	session, _ := c.session(0)
	data := struct {
		EventID string `json:"eventid"`
		UserID  int64  `json:"userid"`
		PlatID  int    `json:"platid"`
		RType   int    `json:"rtype"`
		WASM    int    `json:"wasm"`
		I       string `json:"i"`
		SID     string `json:"sid"`
		EDT     string `json:"edt"`
	}{eventID, session.UserID, 2, 1, 1, "", "", ""}
	result, err := c.request("verificationInfo", http.MethodPost, c.gatewayURL, "/verifyservice/v3/get_verify_info", nil, data, "android", "", false)
	if err != nil {
		writeVerificationJSON(writer, http.StatusBadGateway, map[string]interface{}{"status": 0, "message": err.Error()})
		return
	}
	writeVerificationJSON(writer, http.StatusOK, result)
}

func (c *Client) serveVerificationSubmit(writer http.ResponseWriter, request *http.Request) {
	if !isLoopbackRequest(request) {
		writeVerificationJSON(writer, http.StatusForbidden, map[string]interface{}{"status": 0, "message": "仅允许本机验证"})
		return
	}
	if request.Method != http.MethodPost {
		writeVerificationJSON(writer, http.StatusMethodNotAllowed, map[string]interface{}{"status": 0, "message": "请求方法不支持"})
		return
	}
	var input verificationSubmit
	if json.NewDecoder(io.LimitReader(request.Body, 64<<10)).Decode(&input) != nil || input.EventID == "" || input.VerifyCode == "" {
		writeVerificationJSON(writer, http.StatusBadRequest, map[string]interface{}{"status": 0, "message": "验证参数不完整"})
		return
	}
	result, err := c.submitVerification(input)
	if err != nil {
		writeVerificationJSON(writer, http.StatusBadGateway, map[string]interface{}{"status": 0, "message": err.Error()})
		return
	}
	writeVerificationJSON(writer, http.StatusOK, result)
}

func (c *Client) submitVerification(input verificationSubmit) (map[string]interface{}, error) {
	session, err := c.session(0)
	if err != nil {
		return nil, err
	}
	c.mu.RLock()
	device := c.device
	c.mu.RUnlock()
	sid, edt, err := generateSIDEDT(device, session.UserID)
	if err != nil {
		return nil, &BridgeError{Code: ErrorDecode, Message: "verification fingerprint generation failed", Operation: "verification", Cause: err.Error()}
	}
	paramsText := "{}"
	if input.VType == 32 {
		encoded, _ := json.Marshal(struct {
			Code string `json:"code"`
		}{input.VerifyCode})
		paramsText = string(encoded)
	}
	pk, encryptedParams, err := encryptVerificationParams(paramsText)
	if err != nil {
		return nil, &BridgeError{Code: ErrorDecode, Message: "verification data encryption failed", Operation: "verification", Cause: err.Error()}
	}
	body := map[string]interface{}{"eventid": input.EventID, "userid": session.UserID, "platid": 2, "v_type": input.VType, "wasm": 1, "i": "", "sid": sid, "edt": edt, "pk": pk, "params": encryptedParams}
	if input.VType == 23 {
		body["verifycode"] = input.VerifyCode
	} else if input.VType == 32 {
		body["code"] = input.VerifyCode
	} else {
		return nil, &BridgeError{Code: ErrorInvalidArgument, Message: "unsupported verification type", Operation: "verification"}
	}
	return c.request("verificationSubmit", http.MethodPost, "https://verifyservice.kugou.com", "/v4/verify_user_info", map[string]string{"clientver": "11510"}, body, "android", "", false)
}

func generateSIDEDT(device Device, userID int64) (string, string, error) {
	keySeed := make([]byte, 16)
	if _, err := rand.Read(keySeed); err != nil {
		return "", "", err
	}
	digest := md5.Sum(keySeed)
	key := []byte(hex.EncodeToString(digest[:])[:16])
	plain := fmt.Sprintf("mid=%s;userid=%d;dfid=%s;webgl=%s;webdriver=0;ts=%d;data=%s", device.MID, userID, device.DFID, device.WebGL, time.Now().UnixMilli(), verificationEvents())
	block, err := aes.NewCipher(key)
	if err != nil {
		return "", "", err
	}
	padded := pkcs7Pad([]byte(plain), block.BlockSize())
	encrypted := make([]byte, len(padded))
	cipher.NewCBCEncrypter(block, []byte("kugousecurity123")).CryptBlocks(encrypted, padded)
	publicKey, err := parsePublicKey(sidPublicKey)
	if err != nil {
		return "", "", err
	}
	sidBytes, err := rsa.EncryptOAEP(sha256.New(), rand.Reader, publicKey, key, nil)
	if err != nil {
		return "", "", err
	}
	return base64.StdEncoding.EncodeToString(sidBytes), base64.StdEncoding.EncodeToString(encrypted), nil
}

func encryptVerificationParams(plain string) (string, string, error) {
	tempBytes := make([]byte, 12)
	if _, err := rand.Read(tempBytes); err != nil {
		return "", "", err
	}
	tempKey := strings.ToLower(base64.RawURLEncoding.EncodeToString(tempBytes))[:16]
	digest := md5.Sum([]byte(tempKey))
	keyHex := hex.EncodeToString(digest[:])
	block, err := aes.NewCipher([]byte(keyHex))
	if err != nil {
		return "", "", err
	}
	padded := pkcs7Pad([]byte(plain), block.BlockSize())
	encrypted := make([]byte, len(padded))
	cipher.NewCBCEncrypter(block, []byte(keyHex[16:])).CryptBlocks(encrypted, padded)
	publicKey, err := parsePublicKey(liteVerifyPublicKey)
	if err != nil {
		return "", "", err
	}
	message, _ := json.Marshal(struct {
		Key string `json:"key"`
	}{tempKey})
	size := (publicKey.N.BitLen() + 7) / 8
	paddedMessage := make([]byte, size)
	copy(paddedMessage, message)
	value := new(big.Int).SetBytes(paddedMessage)
	result := new(big.Int).Exp(value, big.NewInt(int64(publicKey.E)), publicKey.N)
	return fmt.Sprintf("%0*x", size*2, result), hex.EncodeToString(encrypted), nil
}

func parsePublicKey(value string) (*rsa.PublicKey, error) {
	block, _ := pem.Decode([]byte(value))
	if block == nil {
		return nil, fmt.Errorf("invalid public key")
	}
	parsed, err := x509.ParsePKIXPublicKey(block.Bytes)
	if err != nil {
		return nil, err
	}
	key, ok := parsed.(*rsa.PublicKey)
	if !ok {
		return nil, fmt.Errorf("invalid RSA public key")
	}
	return key, nil
}

func pkcs7Pad(value []byte, size int) []byte {
	padding := size - len(value)%size
	return append(value, bytesRepeat(byte(padding), padding)...)
}

func bytesRepeat(value byte, count int) []byte {
	result := make([]byte, count)
	for index := range result {
		result[index] = value
	}
	return result
}

func verificationEvents() string {
	sentinel := uint64(math.MaxUint32) - uint64(randomInt(0, 19))
	entries := []string{"5,0,0", fmt.Sprintf("5,%d,0", sentinel), "5,0,0", fmt.Sprintf("5,%d,0", sentinel)}
	timestamp, eventIndex := randomInt(5, 20), 0
	entries = append(entries, fmt.Sprintf("6,%d,%d,750,500", timestamp, eventIndex), fmt.Sprintf("6,%d,%d,750,500", sentinel, eventIndex))
	eventIndex++
	for index := 0; index < 3; index++ {
		timestamp += randomInt(80, 600)
		entries = append(entries, fmt.Sprintf("5,%d,%d", timestamp, eventIndex), fmt.Sprintf("5,%d,%d", sentinel, eventIndex))
		eventIndex++
	}
	sx, sy, ex, ey := randomInt(200, 600), randomInt(200, 500), randomInt(500, 700), randomInt(80, 150)
	points := randomInt(30, 60)
	c1x, c1y := float64(sx)+float64(ex-sx)*.3+float64(randomInt(-80, 80)), float64(sy)+float64(ey-sy)*.2+float64(randomInt(-60, 60))
	c2x, c2y := float64(sx)+float64(ex-sx)*.7+float64(randomInt(-60, 60)), float64(sy)+float64(ey-sy)*.8+float64(randomInt(-40, 40))
	for index := 0; index <= points; index++ {
		t := float64(index) / float64(points)
		u := 1 - t
		x := int(math.Floor(u*u*u*float64(sx) + 3*u*u*t*c1x + 3*u*t*t*c2x + t*t*t*float64(ex) + .5))
		y := int(math.Floor(u*u*u*float64(sy) + 3*u*u*t*c1y + 3*u*t*t*c2y + t*t*t*float64(ey) + .5))
		timestamp += randomInt(8, 50)
		sub := index % 2
		entries = append(entries, fmt.Sprintf("3,%d,%d,%d,%d", timestamp, sub, x, y), fmt.Sprintf("3,%d,%d,%d,%d", sentinel, sub, x, y))
	}
	return strings.Join(entries, ":")
}

func randomInt(minimum, maximum int) int {
	limit := big.NewInt(int64(maximum - minimum + 1))
	value, err := rand.Int(rand.Reader, limit)
	if err != nil {
		return minimum + bits.OnesCount64(uint64(time.Now().UnixNano()))%(maximum-minimum+1)
	}
	return minimum + int(value.Int64())
}

func writeVerificationJSON(writer http.ResponseWriter, status int, value interface{}) {
	writer.Header().Set("Content-Type", "application/json; charset=utf-8")
	writer.WriteHeader(status)
	_ = json.NewEncoder(writer).Encode(value)
}

func isLoopbackRequest(request *http.Request) bool {
	host, _, err := net.SplitHostPort(request.RemoteAddr)
	return err == nil && net.ParseIP(host).IsLoopback()
}

const verificationHTML = `<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>酷狗安全验证</title><style>body{font-family:system-ui;background:#f4f6fb;margin:0;padding:32px}.card{max-width:480px;margin:auto;background:white;padding:28px;border-radius:16px;box-shadow:0 8px 30px #0001}button,input{width:100%;box-sizing:border-box;padding:12px;margin-top:12px;border-radius:8px}button{border:0;background:#5b7cff;color:white}.error{color:#d33}.ok{color:#198754}</style></head><body><div class="card"><h2>酷狗音乐概念版安全验证</h2><p id="status">正在获取验证信息……</p><div id="sms" hidden><input id="code" maxlength="6" placeholder="请输入短信验证码"><button onclick="submitSms()">提交验证码</button></div></div><script>const eventid=new URLSearchParams(location.search).get('eventid');let info;const status=document.getElementById('status');async function json(url,opt){const r=await fetch(url,opt);const x=await r.json();if(!r.ok||Number(x.status)===0)throw new Error(x.message||x.error||x.msg||'请求失败');return x}function load(){return new Promise((ok,no)=>{const s=document.createElement('script');s.src='https://turing.captcha.qcloud.com/TCaptcha.js';s.onload=ok;s.onerror=()=>no(new Error('腾讯验证码加载失败'));document.head.appendChild(s)})}async function submit(code){status.textContent='正在提交验证……';const x=await json('/api/submit',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({eventid,vType:Number(info.v_type),verifycode:code})});status.className='ok';status.textContent='验证成功，请关闭页面并返回播放器重新播放。'}async function submitSms(){try{await submit(document.getElementById('code').value.trim())}catch(e){status.className='error';status.textContent=e.message}}async function start(){try{const x=await json('/api/info?eventid='+encodeURIComponent(eventid));info=x.data||x;if(Number(info.v_type)===23){status.textContent='正在打开腾讯验证码……';await load();new TencentCaptcha(String(info.txappid),async r=>{if(Number(r&&r.ret)!==0)return;try{await submit('KGCodeTX|'+JSON.stringify({ticket:r.ticket,randstr:r.randstr,txappid:String(info.txappid)}))}catch(e){status.className='error';status.textContent=e.message}},{type:'',showHeader:false}).show()}else if(Number(info.v_type)===32){status.textContent='请输入短信验证码';document.getElementById('sms').hidden=false}else throw new Error('暂不支持的验证类型：'+info.v_type)}catch(e){status.className='error';status.textContent=e.message}}start()</script></body></html>`
