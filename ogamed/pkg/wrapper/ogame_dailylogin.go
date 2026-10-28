package wrapper

import (
	"context"
	"os"
	"strings"
	"time"

	"github.com/alaingilbert/ogame/pkg/taskRunner"
	"github.com/alaingilbert/ogame/pkg/utils"
)

const dailyLoginFile = "ogamed_dailylogin.txt"

func readLastDailyLogin() time.Time {
	data, err := os.ReadFile(dailyLoginFile)
	if err != nil {
		return time.Time{}
	}
	t, err := time.Parse(time.RFC3339, strings.TrimSpace(string(data)))
	if err != nil {
		return time.Time{}
	}
	return t
}

func (b *OGame) StartDailyLogin(ctx context.Context) {
	go func() {
		for {
			select {
			case <-ctx.Done():
				return
			case <-time.After(5 * time.Minute):
			}
			if !b.isLoggedIn() || time.Since(readLastDailyLogin()) < 24*time.Hour {
				continue
			}
			token := utils.Or(b.bearerToken, b.getBearerTokenFromCookie())
			if token == "" {
				continue
			}
			if _, _, err := b.WithPriority(taskRunner.Normal).LoginWithBearerToken(token); err != nil {
				b.error("daily login failed:", err)
				continue
			}
			b.debug("daily login done")
			_ = os.WriteFile(dailyLoginFile, []byte(time.Now().Format(time.RFC3339)), 0600)
		}
	}()
}
