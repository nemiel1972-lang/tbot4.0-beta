package wrapper

import (
	"net/http"
	"net/url"
	"sync"
	"time"
)


const browserSrcKey = "_tbsrc"

type CPStats struct {
	BotSwitch   int64
	BotSame     int64
	UserSwitch  int64
	UserSame    int64
	BotOnUser   int64
	BotRoutes   map[string]int64
	HumanClicks int64
	BrowserAuto int64
}

var (
	cpMu      sync.Mutex
	cpLast    string
	cpHumanAt time.Time
	cpStat    = CPStats{BotRoutes: map[string]int64{}}
)

const cpHumanWindow = 2 * time.Minute

func markBrowser(method string, vals url.Values) {
	vals.Set(browserSrcKey, "browser")
	human := method == http.MethodPost || !IsAjaxPage(vals)
	cpMu.Lock()
	if human {
		cpStat.HumanClicks++
		cpHumanAt = time.Now()
	} else {
		cpStat.BrowserAuto++
	}
	cpMu.Unlock()
}

func noteCP(method string, vals url.Values, fromBrowser bool) {
	cp := vals.Get("cp")
	if cp == "" {
		return
	}
	cpMu.Lock()
	defer cpMu.Unlock()
	sw := cp != cpLast
	switch {
	case fromBrowser && sw:
		cpStat.UserSwitch++
	case fromBrowser:
		cpStat.UserSame++
	case sw:
		cpStat.BotSwitch++
		cpStat.BotRoutes[outKey(method, vals)]++
		if time.Since(cpHumanAt) < cpHumanWindow {
			cpStat.BotOnUser++
		}
	default:
		cpStat.BotSame++
	}
	cpLast = cp
}

func SnapshotCP() CPStats {
	cpMu.Lock()
	defer cpMu.Unlock()
	s := cpStat
	cpStat = CPStats{BotRoutes: map[string]int64{}}
	return s
}

