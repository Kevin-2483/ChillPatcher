package api

import (
	"crypto/md5"
	"encoding/hex"
	"fmt"
	"sort"
	"strings"
)

const (
	androidSecret = "LnT6xpN3khm36zse0QzvmgTZ3waWdRSA"
	webSecret     = "NVPh5oo715z5DIWAeQlhMDsWXXQV4hwt"
	urlKeySecret  = "185672dd44712f60bb1736df5a377e82"
)

func md5Hex(value string) string {
	sum := md5.Sum([]byte(value))
	return hex.EncodeToString(sum[:])
}

func signature(params map[string]string, body, secret string) string {
	keys := make([]string, 0, len(params))
	for key := range params {
		if key != "signature" {
			keys = append(keys, key)
		}
	}
	sort.Strings(keys)
	value := secret
	for _, key := range keys {
		value += key + "=" + params[key]
	}
	return md5Hex(value + body + secret)
}

func songURLKey(hash, mid string, userID int64) string {
	return md5Hex(fmt.Sprintf("%s%s%d%s%d", strings.ToLower(hash), urlKeySecret, appID, mid, userID))
}
