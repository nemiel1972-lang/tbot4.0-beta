package ogame

type Fields struct {
	Built int64
	Total int64
}

func (f Fields) HasFieldAvailable() bool {
	return f.Built < f.Total
}
