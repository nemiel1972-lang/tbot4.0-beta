package ogame

import (
	"strconv"
	"time"
)

type MessagesTabID int64

type CelestialID int64

type MoonID CelestialID

func (m MoonID) Celestial() CelestialID {
	return CelestialID(m)
}

type Speed float64

func (s Speed) Float64() float64 {
	return float64(s)
}

func (s Speed) Int64() int64 {
	return int64(s)
}

func (s Speed) Int() int64 {
	return int64(s)
}

func (s Speed) String() string {
	switch s {
	case FivePercent:
		return "5%"
	case TenPercent:
		return "10%"
	case FifteenPercent:
		return "15%"
	case TwentyPercent:
		return "20%"
	case TwentyFivePercent:
		return "25%"
	case ThirtyPercent:
		return "30%"
	case ThirtyFivePercent:
		return "35%"
	case FourtyPercent:
		return "40%"
	case FourtyFivePercent:
		return "45%"
	case FiftyPercent:
		return "50%"
	case FiftyFivePercent:
		return "55%"
	case SixtyPercent:
		return "60%"
	case SixtyFivePercent:
		return "65%"
	case SeventyPercent:
		return "70%"
	case SeventyFivePercent:
		return "75%"
	case EightyPercent:
		return "80%"
	case EightyFivePercent:
		return "85%"
	case NinetyPercent:
		return "90%"
	case NinetyFivePercent:
		return "95%"
	case HundredPercent:
		return "100%"
	default:
		return strconv.FormatFloat(float64(s), 'f', 1, 64)
	}
}

type ResourcesResp struct {
	Metal struct {
		Resources struct {
			ActualFormat string
			Actual       int64
			Max          int64
			Production   float64
		}
		Tooltip string
		Class   string
	}
	Crystal struct {
		Resources struct {
			ActualFormat string
			Actual       int64
			Max          int64
			Production   float64
		}
		Tooltip string
		Class   string
	}
	Deuterium struct {
		Resources struct {
			ActualFormat string
			Actual       int64
			Max          int64
			Production   float64
		}
		Tooltip string
		Class   string
	}
	Energy struct {
		Resources struct {
			ActualFormat string
			Actual       int64
		}
		Tooltip string
		Class   string
	}
	Darkmatter struct {
		Resources struct {
			ActualFormat string
			Actual       int64
		}
		String  string
		Tooltip string
	}
	HonorScore int64
}

type planetResource struct {
	Input struct {
		Metal     int64
		Crystal   int64
		Deuterium int64
	}
	Output struct {
		Metal     int64
		Crystal   int64
		Deuterium int64
	}
	IsMoon        bool
	ImageFileName string
	Name          string
}

type PlanetResources map[CelestialID]planetResource

type Multiplier struct {
	Metal     float64
	Crystal   float64
	Deuterium float64
	Honor     float64
}

type EspionageReportType int

const Action EspionageReportType = 0

const Report EspionageReportType = 1

type CombatReportSummary struct {
	ID           int64
	APIKey       string
	FleetID      FleetID
	Origin       *Coordinate
	Destination  Coordinate
	AttackerName string
	DefenderName string
	Loot         int64
	Metal        int64
	Crystal      int64
	Deuterium    int64
	DebrisField  int64
	CreatedAt    time.Time
}

type EspionageReportSummary struct {
	ID             int64
	Type           EspionageReportType
	From           string
	Target         Coordinate
	LootPercentage float64
}

type ExpeditionMessage struct {
	ID         int64
	Coordinate Coordinate
	Content    string
	Resources  Resources
	Ships      ShipsInfos
	CreatedAt  time.Time
}

type MarketplaceMessage struct {
	ID                  int64
	Type                int64
	CreatedAt           time.Time
	Token               string
	MarketTransactionID int64
}

type Preferences struct {
	SpioAnz                            int64
	SpySystemAutomaticQuantity         int64
	SpySystemTargetPlanetTypes         int64
	SpySystemTargetPlayerTypes         int64
	SpySystemIgnoreSpiedInLastXMinutes int64
	DisableChatBar                     bool
	DisableOutlawWarning               bool
	MobileVersion                      bool
	ShowOldDropDowns                   bool
	ActivateAutofocus                  bool
	Language                           string
	EventsShow                         int64
	SortSetting                        int64
	SortOrder                          int64
	ShowDetailOverlay                  bool
	AnimatedSliders                    bool
	AnimatedOverview                   bool
	PopupsNotices                      bool
	PopopsCombatreport                 bool
	SpioReportPictures                 bool
	MsgResultsPerPage                  int64
	AuctioneerNotifications            bool
	EconomyNotifications               bool
	ShowActivityMinutes                bool
	PreserveSystemOnPlanetChange       bool
	DiscoveryWarningEnabled            bool
	UrlaubsModus                       bool

	Notifications struct {
		BuildList               bool
		FriendlyFleetActivities bool
		HostileFleetActivities  bool
		ForeignEspionage        bool
		AllianceBroadcasts      bool
		AllianceMessages        bool
		Auctions                bool
		Account                 bool
	}
}

type ACSValues struct {
	Galaxy        int64
	System        int64
	Position      int64
	CelestialType CelestialType
	Name          string
	ACSValues     string
	Union         int64
}

type Relocation struct {
	MoveLink             string
	PlanetMovePossible   bool
	SufficientDarkMatter bool
	MissionType          MissionID
	DarkMatterCost       int64
}

type MinifleetResponse struct {
	Response struct {
		Message     string `json:"message"`
		Type        int    `json:"type"`
		Slots       int    `json:"slots"`
		Probes      int    `json:"probes"`
		Recyclers   int    `json:"recyclers"`
		Explorers   int    `json:"explorers"`
		Missiles    int    `json:"missiles"`
		ShipsSent   int    `json:"shipsSent"`
		Coordinates struct {
			Galaxy   int `json:"galaxy"`
			System   int `json:"system"`
			Position int `json:"position"`
		} `json:"coordinates"`
		PlanetType int  `json:"planetType"`
		Success    bool `json:"success"`
	} `json:"response"`
	NewAjaxToken string `json:"newAjaxToken"`
}
