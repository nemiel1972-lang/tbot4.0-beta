package wrapper

import (
	"net/url"
	"sort"
	"sync"
)

var (
	outMu      sync.Mutex
	outAll     = map[string]int64{}
	outBrowser = map[string]int64{}
)

func outKey(method string, vals url.Values) string {
	k := method + " " + vals.Get("page")
	if c := vals.Get("component"); c != "" {
		k += "/" + c
	}
	if a := vals.Get("action"); a != "" {
		k += "/" + a
	}
	return k
}

func countOut(method string, vals url.Values) {
	k := outKey(method, vals)
	outMu.Lock()
	outAll[k]++
	outMu.Unlock()
}

func CountBrowser(method string, vals url.Values) {
	markBrowser(method, vals)
	k := outKey(method, vals)
	outMu.Lock()
	outBrowser[k]++
	outMu.Unlock()
}

type OutEntry struct {
	Key     string
	All     int64
	Browser int64
}

func SnapshotOut() (entries []OutEntry, total, browser int64) {
	outMu.Lock()
	all, br := outAll, outBrowser
	outAll, outBrowser = map[string]int64{}, map[string]int64{}
	outMu.Unlock()
	for k, v := range all {
		entries = append(entries, OutEntry{Key: k, All: v, Browser: br[k]})
		total += v
	}
	for _, v := range br {
		browser += v
	}
	sort.Slice(entries, func(i, j int) bool { return entries[i].All > entries[j].All })
	return
}
