package ogame

import "encoding/json"

type Item struct {
	Ref            string
	Name           string
	Image          string
	ImageLarge     string
	Title          string
	Rarity         string
	Amount         int64
	AmountFree     int64
	AmountBought   int64
	Extra          map[string]any `json:"extra,omitempty"`
	canBeActivated bool
}

type ActiveItem struct {
	ID            int64
	Ref           string
	Name          string
	TimeRemaining int64
	TotalDuration int64
	ImgSmall      string
}

var knownItemKeys = map[string]bool{"ref": true, "name": true, "image": true, "imageLarge": true, "title": true, "rarity": true, "amount": true, "amount_free": true, "amount_bought": true}

func ParseInventoryItems(raw []byte) ([]Item, error) {
	var typed map[string]Item
	if err := json.Unmarshal(raw, &typed); err != nil {
		return nil, err
	}
	var generic map[string]map[string]any
	_ = json.Unmarshal(raw, &generic)
	items := make([]Item, 0, len(typed))
	for k, it := range typed {
		if g, ok := generic[k]; ok {
			for key, v := range g {
				if knownItemKeys[key] {
					continue
				}
				switch val := v.(type) {
				case string:
					if len(val) > 400 {
						continue
					}
				case float64, bool:
				default:
					continue
				}
				if it.Extra == nil {
					it.Extra = map[string]any{}
				}
				it.Extra[key] = v
			}
		}
		items = append(items, it)
	}
	return items, nil
}
