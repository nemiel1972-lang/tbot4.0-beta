package wrapper

import (
	"strings"
	"testing"

	"github.com/PuerkitoBio/goquery"
)

const auctionHistorySample = `<div class="history_content"><ul>
<li class="even "> <a href="javascript:void(0);" class="slideIn" ref="27cbcd52f16693023cb966e5026d8a1efbbfc0f9"> <img height="30" width="30" src="/cdn/img/item-images/d0b8.png" alt="" title="DETROID Silber|Verringert die Bauzeit um &lt;span style=&#34;font-weight: bold;&#34;&gt;2h&lt;/span&gt;.&lt;br /&gt; Im Inventar: 20" class="item_img tooltipHTML tooltipLeft r_uncommon"/> </a> <span class="detail sum">10.000.000</span> <span class="detail player"> <a href="https://x/game/index.php?page=ingame&amp;component=galaxy&amp;galaxy=1&amp;system=35"> Fogan </a> </span> <span class="detail date_time">21:35:41</span> </li>
<li class="even "><a href="javascript:void(0);" class="slideIn" ref="3c9f"><img class="item_img tooltipHTML" alt="" data-tooltip-title="Kristall-Booster Bronze|&amp;#43;10% mehr"></a>
<span class="detail sum">6.001</span>
<span class="detail player"><a href="x">
 Walters_Willy
</a></span>
<span class="detail date_time">19:35:56</span></li>
<li class="odd more_auctions_li" style="display: none;"><a class="slideIn" ref="929d"><img class="item_img" alt="" data-tooltip-title="KRAKEN Gold|text"></a>
<span class="detail sum">37.501.000</span>
<span class="detail player"><a>Senti982</a></span>
<span class="detail date_time">18:43:58</span></li>
</ul></div>`

func TestParseAuctionHistory(t *testing.T) {
	doc, err := goquery.NewDocumentFromReader(strings.NewReader(auctionHistorySample))
	if err != nil {
		t.Fatal(err)
	}
	h := parseAuctionHistory(doc)
	if len(h) != 3 {
		t.Fatalf("erwartet 3 Zeilen, erhalten %d", len(h))
	}
	if h[0].Item != "detroid silber" || h[0].Sum != 10000000 || h[0].Player != "Fogan" || h[0].Time != "21:35:41" {
		t.Fatalf("echte Zeile falsch: %+v", h[0])
	}
	h = h[1:]
	if h[0].Item != "kristall-booster bronze" || h[0].Sum != 6001 || h[0].Player != "Walters_Willy" || h[0].Time != "19:35:56" || h[0].Ref != "3c9f" {
		t.Fatalf("erste Zeile falsch: %+v", h[0])
	}
	if h[1].Item != "kraken gold" || h[1].Sum != 37501000 || h[1].Player != "Senti982" {
		t.Fatalf("zweite Zeile falsch: %+v", h[1])
	}
}
