package wrapper

import (
	"strings"

	"github.com/PuerkitoBio/goquery"
	"github.com/alaingilbert/ogame/pkg/ogame"
	"github.com/alaingilbert/ogame/pkg/utils"
)

func parseAuctionHistory(doc *goquery.Document) []ogame.AuctionHistoryEntry {
	out := make([]ogame.AuctionHistoryEntry, 0, 20)
	doc.Find("div.history_content ul li").Each(func(_ int, li *goquery.Selection) {
		img := li.Find("img.item_img")
		tip := img.AttrOr("title", img.AttrOr("data-tooltip-title", ""))
		name := strings.ToLower(strings.TrimSpace(strings.SplitN(tip, "|", 2)[0]))
		player := strings.TrimSpace(li.Find("span.detail.player").Text())
		sum := utils.ParseInt(li.Find("span.detail.sum").Text())
		if name == "" || player == "" || sum <= 0 {
			return
		}
		out = append(out, ogame.AuctionHistoryEntry{
			Item:   name,
			Ref:    li.Find("a.slideIn").AttrOr("ref", ""),
			Sum:    sum,
			Player: player,
			Time:   strings.TrimSpace(li.Find("span.detail.date_time").Text()),
		})
	})
	return out
}
