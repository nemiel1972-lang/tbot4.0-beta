package wrapper

import (
	"errors"
	"net/http"
	"strconv"
	"sync/atomic"
	"time"

	"github.com/labstack/echo/v4"
)

var ErrLoginPaused = errors.New("login paused to protect the IP (too many failed logins)")

var loginPausedUntil atomic.Int64

func loginPauseCheck() error {
	if until := loginPausedUntil.Load(); until > time.Now().Unix() {
		return ErrLoginPaused
	}
	return nil
}

func ClearLoginPause() {
	loginPausedUntil.Store(0)
}

type LoginPauseStatus struct {
	PausedUntil int64
	LoggedIn    bool
}

func LoginPauseHandler(c echo.Context) error {
	bot := c.Get("bot").(*OGame)
	if c.Request().Method == http.MethodPost {
		until, err := strconv.ParseInt(c.FormValue("until"), 10, 64)
		if err != nil || until < 0 {
			return c.JSON(http.StatusBadRequest, ErrorResp(400, "until must be unix seconds (0 = no pause)"))
		}
		loginPausedUntil.Store(until)
	}
	return c.JSON(http.StatusOK, SuccessResp(LoginPauseStatus{PausedUntil: loginPausedUntil.Load(), LoggedIn: bot.IsLoggedIn()}))
}
