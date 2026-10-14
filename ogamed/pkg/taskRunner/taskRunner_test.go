package taskRunner

import (
	"fmt"
	"time"
)

type testItem struct {
	taskDoneCh chan struct{}
}

func (i *testItem) SetTaskDoneCh(ch chan struct{}) {
	i.taskDoneCh = ch
}

func (i *testItem) DoSomething(name string) {
	defer close(i.taskDoneCh)
	time.Sleep(250 * time.Millisecond)
	fmt.Println("Do something " + name)
}

func (i *testItem) DoSomethingElse(name string) {
	defer close(i.taskDoneCh)
	time.Sleep(250 * time.Millisecond)
	fmt.Println("Do something else " + name)
}

