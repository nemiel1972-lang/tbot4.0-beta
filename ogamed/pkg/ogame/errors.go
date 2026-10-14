package ogame

import (
	"errors"
	"fmt"
	"time"
)

var ErrNotLogged = errors.New("not logged")

var ErrMobileView = errors.New("mobile view not supported")

var ErrInvalidPlanetID = errors.New("invalid planet id")

var ErrAllSlotsInUse = errors.New("all slots are in use")

var ErrBotInactive = errors.New("bot is not active")

var ErrBotLoggedOut = errors.New("bot is logged out")

var ErrDeactivateHidePictures = errors.New("deactivate 'Hide pictures in reports'")

var ErrEventsBoxNotDisplayed = errors.New("eventList box is not displayed")

var ErrNotEnoughDeuterium = errors.New("not enough deuterium")

var (
	ErrUnionNotFound                      = errors.New("union not found")
	ErrAccountInVacationMode              = errors.New("account in vacation mode")
	ErrNoShipSelected                     = errors.New("no ships to send")
	ErrUninhabitedPlanet                  = errors.New("uninhabited planet")
	ErrNoDebrisField                      = errors.New("no debris field")
	ErrPlayerInVacationMode               = errors.New("player in vacation mode")
	ErrAdminOrGM                          = errors.New("admin or GM")
	ErrNoAstrophysics                     = errors.New("you have to research Astrophysics first")
	ErrNoobProtection                     = errors.New("noob protection")
	ErrPlayerTooStrong                    = errors.New("this planet can not be attacked as the player is to strong")
	ErrNoMoonAvailable                    = errors.New("no moon available")
	ErrNoRecyclerAvailable                = errors.New("no recycler available")
	ErrNoEventsRunning                    = errors.New("there are currently no events running")
	ErrPlanetAlreadyReservedForRelocation = errors.New("this planet has already been reserved for a relocation")
	ErrNotEnoughFuel                      = errors.New("not enough fuel")
	ErrAttackBannedUntil                  = errors.New("attack ban until")
	ErrNotEnoughCargoSpace                = errors.New("not enough cargo space")
	ErrNotEnoughShips                     = errors.New("not enough ships to send")
	ErrEngagedInCombat                    = errors.New("the fleet is currently engaged in combat")
)

type AttackBlockActivatedErr struct {
	BlockedUntil time.Time
}

func (a *AttackBlockActivatedErr) Error() string {
	return fmt.Sprintf("account block activated : %s", a.BlockedUntil.Format(time.DateTime))
}

func NewAttackBlockActivatedErr(blockedUntil time.Time) *AttackBlockActivatedErr {
	return &AttackBlockActivatedErr{BlockedUntil: blockedUntil}
}
