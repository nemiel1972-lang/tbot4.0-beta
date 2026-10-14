package ogame

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
