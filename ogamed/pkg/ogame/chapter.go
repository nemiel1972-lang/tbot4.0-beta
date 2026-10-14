package ogame

type Chapter struct {
	ID       int64
	ClaimAll *int64
	Tasks    []ChapterTask
}

type ChapterTask struct {
	ID        int64
	Completed bool
	Collected bool
}
