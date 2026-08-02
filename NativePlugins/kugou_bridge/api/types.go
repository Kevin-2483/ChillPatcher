package api

import (
	"encoding/json"
	"fmt"
)

const (
	ErrorInvalidArgument = 1001
	ErrorNotInitialized  = 1002
	ErrorNotLoggedIn     = 1003
	ErrorSessionNotFound = 1004
	ErrorNetwork         = 2001
	ErrorHTTP            = 2002
	ErrorAPI             = 2003
	ErrorDecode          = 2004
	ErrorStorage         = 3001
)

type BridgeError struct {
	Code            int    `json:"code"`
	Message         string `json:"message"`
	Operation       string `json:"operation,omitempty"`
	HTTPStatus      int    `json:"httpStatus,omitempty"`
	APIErrorCode    int    `json:"apiCode,omitempty"`
	SSACode         string `json:"ssaCode,omitempty"`
	VerificationURL string `json:"verificationUrl,omitempty"`
	Cause           string `json:"cause,omitempty"`
}

func (e *BridgeError) Error() string {
	if e.Operation == "" {
		return e.Message
	}
	return fmt.Sprintf("%s: %s", e.Operation, e.Message)
}

type Device struct {
	DFID                string `json:"dfid"`
	MID                 string `json:"mid"`
	UUID                string `json:"uuid"`
	WebGL               string `json:"webgl"`
	Registered          bool   `json:"registered"`
	RegistrationVersion int    `json:"registrationVersion,omitempty"`
}

type Session struct {
	UserID   int64  `json:"userid"`
	Token    string `json:"token"`
	VIPType  int    `json:"vipType,omitempty"`
	VIPToken string `json:"vipToken,omitempty"`
	Nickname string `json:"nickname,omitempty"`
	Avatar   string `json:"avatar,omitempty"`
	Updated  int64  `json:"updated"`
}

type SessionState struct {
	ActiveUserID int64     `json:"activeUserId"`
	Accounts     []Session `json:"accounts"`
}

type QRCode struct {
	Key string `json:"key"`
	URL string `json:"url"`
}

type QRStatus struct {
	Status  int             `json:"status"`
	Session *Session        `json:"session,omitempty"`
	Data    json.RawMessage `json:"data,omitempty"`
}

type PageResult struct {
	Page     int             `json:"page"`
	PageSize int             `json:"pageSize"`
	UserID   int64           `json:"userid,omitempty"`
	Data     json.RawMessage `json:"data"`
}

type SongURLRequest struct {
	Hash         string `json:"hash"`
	AlbumAudioID int64  `json:"albumAudioId,omitempty"`
	AlbumID      int64  `json:"albumId,omitempty"`
	Quality      string `json:"quality,omitempty"`
}

type LyricRequest struct {
	Hash         string `json:"hash"`
	AlbumAudioID int64  `json:"albumAudioId,omitempty"`
	Keywords     string `json:"keywords,omitempty"`
	Format       string `json:"format,omitempty"`
}

type FavoriteRequest struct {
	ListID       int64  `json:"listId"`
	FileID       int64  `json:"fileId,omitempty"`
	Hash         string `json:"hash"`
	Name         string `json:"name"`
	AlbumID      int64  `json:"albumId,omitempty"`
	AlbumAudioID int64  `json:"albumAudioId,omitempty"`
}
