package solvers

import (
	"bytes"
	"context"
	"fmt"
	"github.com/alaingilbert/ogame/pkg/gameforge"
	"image"
	"image/color"
	"image/draw"
	"image/png"
	"time"

	"github.com/bwmarrin/discordgo"
)

func DiscordSolver(token string, ownerID string) gameforge.CaptchaSolver {
	return func(ctx context.Context, question, icons []byte) (int64, error) {
		bot, err := discordgo.New("Bot " + token)
		if err != nil {
			return -1, err
		}

		channel, err := bot.UserChannelCreate(ownerID, discordgo.WithContext(ctx))
		if err != nil {
			return -1, err
		}

		answerCh := make(chan int64)

		rmHandlerFn := bot.AddHandler(handleInteraction(ctx, answerCh))
		defer rmHandlerFn()

		if err := bot.Open(); err != nil {
			return -1, err
		}
		defer bot.Close()

		embedImg, err := buildEmbedImg(question, icons)
		if err != nil {
			return -1, err
		}

		msg, err := sendImageWithSelectMenu(ctx, bot, channel, embedImg)
		if err != nil {
			return -1, err
		}

		fmt.Print("Wait for reaction to solve challenge ... ")
		var answer int64
		select {
		case answer = <-answerCh:
		case <-ctx.Done():
			return -1, ctx.Err()
		}
		fmt.Printf("Selected answer : %d.\n", answer)

		go func(msg *discordgo.Message) {
			select {
			case <-time.After(30 * time.Second):
			case <-ctx.Done():
				return
			}
			_ = bot.ChannelMessageDelete(msg.ChannelID, msg.ID, discordgo.WithContext(ctx))
		}(msg)

		return answer, nil
	}
}

func buildEmbedImg(question, icons []byte) (out []byte, err error) {
	const topPadding = 5
	questionImage, err := png.Decode(bytes.NewReader(question))
	if err != nil {
		return nil, err
	}
	iconsImage, err := png.Decode(bytes.NewReader(icons))
	if err != nil {
		return nil, err
	}
	questionBounds := questionImage.Bounds()
	iconsBounds := iconsImage.Bounds()
	resultWidth := max(questionBounds.Max.X, iconsBounds.Max.X)
	resultHeight := questionBounds.Max.Y + iconsBounds.Max.Y + topPadding
	bottomRightPosition := image.Point{X: resultWidth, Y: resultHeight}
	img := image.NewRGBA(image.Rectangle{Max: bottomRightPosition})
	draw.Draw(img, img.Bounds(), &image.Uniform{C: color.RGBA{A: 255}}, image.Point{}, draw.Src)
	questionBounds = questionBounds.Add(image.Point{Y: topPadding})
	draw.Draw(img, questionBounds, questionImage, image.Point{}, draw.Over)
	iconsStartX := (questionBounds.Max.X - iconsBounds.Max.X) / 2
	iconsBounds = iconsBounds.Add(image.Point{X: iconsStartX, Y: questionBounds.Max.Y})
	draw.Draw(img, iconsBounds, iconsImage, image.Point{}, draw.Src)
	buffer := new(bytes.Buffer)
	if err = png.Encode(buffer, img); err != nil {
		return nil, err
	}
	return buffer.Bytes(), nil
}

const (
	discordBtnOpt1 = "btn_option1"
	discordBtnOpt2 = "btn_option2"
	discordBtnOpt3 = "btn_option3"
	discordBtnOpt4 = "btn_option4"
)

func handleInteraction(ctx context.Context, answerCh chan int64) func(*discordgo.Session, *discordgo.InteractionCreate) {
	return func(bot *discordgo.Session, i *discordgo.InteractionCreate) {
		if i.Type == discordgo.InteractionMessageComponent {
			var answer int64
			switch i.MessageComponentData().CustomID {
			case discordBtnOpt1:
				answer = 0
			case discordBtnOpt2:
				answer = 1
			case discordBtnOpt3:
				answer = 2
			case discordBtnOpt4:
				answer = 3
			default:
				answer = -1
			}
			_ = bot.InteractionRespond(i.Interaction, &discordgo.InteractionResponse{
				Type: discordgo.InteractionResponseChannelMessageWithSource,
				Data: &discordgo.InteractionResponseData{
					Content: fmt.Sprintf("You selected image number %d.", answer+1),
					Flags:   discordgo.MessageFlagsEphemeral,
				},
			})
			select {
			case answerCh <- answer:
			case <-ctx.Done():
				return
			}
		}
	}
}

func sendImageWithSelectMenu(ctx context.Context, bot *discordgo.Session, channel *discordgo.Channel, img []byte) (*discordgo.Message, error) {
	buttons := discordgo.ActionsRow{
		Components: []discordgo.MessageComponent{
			discordgo.Button{Label: "1", Style: discordgo.SecondaryButton, CustomID: discordBtnOpt1},
			discordgo.Button{Label: "2", Style: discordgo.SecondaryButton, CustomID: discordBtnOpt2},
			discordgo.Button{Label: "3", Style: discordgo.SecondaryButton, CustomID: discordBtnOpt3},
			discordgo.Button{Label: "4", Style: discordgo.SecondaryButton, CustomID: discordBtnOpt4},
		},
	}
	message := &discordgo.MessageSend{
		Embeds:     []*discordgo.MessageEmbed{{Title: "Select the image to answer the captcha !", Image: &discordgo.MessageEmbedImage{URL: "attachment://image.png"}}},
		Components: []discordgo.MessageComponent{buttons},
		Files:      []*discordgo.File{{Name: "image.png", Reader: bytes.NewReader(img)}},
	}
	return bot.ChannelMessageSendComplex(channel.ID, message, discordgo.WithContext(ctx))
}
