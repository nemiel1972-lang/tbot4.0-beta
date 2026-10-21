package v13_0_0

import (
	"regexp"
	"strconv"
	"strings"

	"github.com/PuerkitoBio/goquery"
	"github.com/alaingilbert/ogame/pkg/ogame"
	"github.com/alaingilbert/ogame/pkg/utils"
	"golang.org/x/net/html"
)

func extractLfBonusesFromDoc(doc *goquery.Document) (ogame.LfBonuses, error) {
	b := ogame.NewLfBonuses()
	ships := doc.Find(`bonus-item-content[data-toggable-target="ships"]`).First()
	costReduction := doc.Find(`bonus-item-content[data-toggable-target="costreduction"]`).First()
	expedition := doc.Find(`bonus-item-content[data-toggable-target="expedition"]`).First()
	characterClasses := doc.Find(`bonus-item-content[data-toggable-target="characterclasses"]`).First()

	for _, heading := range directLfBonusHeadings(ships).EachIter() {
		extractShipStatBonus(heading, b, heading.AttrOr("data-toggable", ""))
	}
	for _, heading := range directLfBonusHeadings(costReduction).EachIter() {
		id := heading.AttrOr("data-toggable", "")
		extractCostReductionBonus(heading, b, id)
		extractTimeReductionBonus(heading, b, id)
	}
	for _, heading := range directLfBonusHeadings(expedition).EachIter() {
		key := heading.AttrOr("data-toggable", "")
		if key != "" {
			match := regexp.MustCompile(`[-+]?\d+(?:[.,]\d+)?\s*%`).FindString(heading.Find("div.subCategoryBonus").First().Text())
			if match != "" {
				b.ExpeditionBonuses[key] = extractBonusFromStringPercentage(match)
			}
		}
		if heading.AttrOr("data-toggable", "") == "ResultBooster" {
			b.LfResourceBonuses.ResourcesExpedition = extractBonusFromStringPercentage(heading.Find("div.subCategoryBonus").First().Text())
		}
	}
	for _, heading := range directLfBonusHeadings(characterClasses).EachIter() {
		if heading.AttrOr("data-toggable", "") == "603" {
			match := regexp.MustCompile(`[-+]?\d+(?:[.,]\d+)?`).FindString(heading.Find("div.subCategoryBonus").First().Text())
			b.CharacterClassesBonuses.Characterclasses3 = extractBonusFromStringPercentage(match)
		}
	}
	return *b, nil
}

func directLfBonusHeadings(section *goquery.Selection) *goquery.Selection {
	return section.ChildrenFiltered("bonus-item-content-holder").ChildrenFiltered("inner-bonus-item-heading[data-toggable]")
}

func extractShipStatBonus(s *goquery.Selection, b *ogame.LfBonuses, subcategory string) {
	i := utils.DoParseI64(subcategory)
	id := ogame.ID(i)
	if !id.IsShip() {
		return
	}
	for _, s := range s.Find("bonus-items").EachIter() {
		extractFn := func(idx int) float64 {
			txt := s.Children().Eq(idx).Contents().FilterFunction(func(i int, s *goquery.Selection) bool {
				return s.Nodes[0].Type == html.TextNode
			}).Text()
			return extractBonusFromStringPercentage(txt)
		}
		shipBonus := ogame.LfShipBonus{
			ID:                  id,
			StructuralIntegrity: extractFn(0),
			ShieldPower:         extractFn(1),
			WeaponPower:         extractFn(2),
			Speed:               extractFn(3),
			CargoCapacity:       extractFn(4),
			FuelConsumption:     extractFn(5),
		}
		b.LfShipBonuses[id] = shipBonus
	}
}

func extractCostReductionBonus(s *goquery.Selection, l *ogame.LfBonuses, subcategory string) {
	i := utils.DoParseI64(subcategory)
	id := ogame.ID(i)
	for _, s := range s.Find("bonus-items").EachIter() {
		txt := s.Eq(0).Children().Eq(0).Contents().FilterFunction(func(i int, s *goquery.Selection) bool {
			return s.Nodes[0].Type == html.TextNode
		}).Text()
		costTimeBonus := l.CostTimeBonuses[id]
		costTimeBonus.Cost = extractBonusFromStringPercentage(txt)
		l.CostTimeBonuses[id] = costTimeBonus
	}
}

func extractTimeReductionBonus(s *goquery.Selection, l *ogame.LfBonuses, subcategory string) {
	i := utils.DoParseI64(subcategory)
	id := ogame.ID(i)
	for _, s := range s.Find("bonus-items").EachIter() {
		txt := s.Eq(0).Children().Eq(1).Contents().FilterFunction(func(i int, s *goquery.Selection) bool {
			return s.Nodes[0].Type == html.TextNode
		}).Text()
		costTimeBonus := l.CostTimeBonuses[id]
		costTimeBonus.Duration = extractBonusFromStringPercentage(txt)
		l.CostTimeBonuses[id] = costTimeBonus
	}
}

func extractBonusFromStringPercentage(s string) float64 {
	v := strings.Replace(s, "%", "", 1)
	return extractBonusFromString(v) / 100.0
}

func extractBonusFromString(s string) float64 {
	v := strings.TrimSpace(s)
	v = strings.Replace(v, ",", ".", 1)
	b, _ := strconv.ParseFloat(v, 64)
	return utils.RoundThousandth(b)
}
