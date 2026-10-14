package ogame

import (
	"github.com/alaingilbert/ogame/pkg/utils"
	"strconv"
)

type DMCost struct {
	Cost                int64
	CanBuy              bool
	Complete            bool
	OGameID             ID
	Nbr                 int64
	BuyAndActivateToken string
	Token               string
}

func (d DMCost) String() string {
	return "\n" +
		"               Cost: " + utils.FI64(d.Cost) + "\n" +
		"             CanBuy: " + strconv.FormatBool(d.CanBuy) + "\n" +
		"           Complete: " + strconv.FormatBool(d.Complete) + "\n" +
		"            OGameID: " + utils.FI64(d.OGameID) + "\n" +
		"                Nbr: " + utils.FI64(d.Nbr) + "\n" +
		"BuyAndActivateToken: " + d.BuyAndActivateToken + "\n" +
		"              Token: " + d.Token
}

type DMCosts struct {
	Buildings DMCost
	Research  DMCost
	Shipyard  DMCost
}

func (d DMCosts) String() string {
	return "\n" +
		"Buildings:" + d.Buildings.String() + "\n" +
		"Research:" + d.Research.String() + "\n" +
		"Shipyard:" + d.Shipyard.String()
}
