package ogame

type AuctioneerNewBid struct {
	Sum       int64
	Price     int64
	Bids      int64
	AuctionID int64
	Player    struct {
		ID   int64
		Name string
		Link string
	}
}

type AuctioneerNewAuction struct {
	AuctionID int64
	Approx    int64
}

type AuctioneerAuctionFinished struct {
	Sum         int64
	Bids        int64
	NextAuction int64
	Time        string
	Player      struct {
		ID   int64
		Name string
		Link string
	}
}

type AuctioneerTimeRemaining struct {
	Approx int64
}

type AuctioneerNextAuction struct {
	Secs int64
}
