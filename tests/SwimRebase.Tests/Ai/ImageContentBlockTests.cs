using Ai.Application.Models;

namespace SwimRebase.Tests.Ai;

public class ImageContentBlockTests
{
    [Fact]
    public void ImageId_round_trips_through_stored_content_json()
    {
        var imageId = Guid.NewGuid();
        var json = ChatContentJson.Serialize([new TextContentBlock("hi"), new ImageContentBlock("boards/a.png", "image/png", imageId)]);

        var image = Assert.IsType<ImageContentBlock>(ChatContentJson.Deserialize(json)[1]);

        Assert.Equal(imageId, image.ImageId);
        Assert.Equal("boards/a.png", image.ObjectKey);
    }

    [Fact]
    public void Content_json_with_the_discriminator_not_first_deserializes()
    {
        var imageId = Guid.NewGuid();
        var jsonb = $$"""[{"Text": "hi", "$type": "text"}, {"ImageId": "{{imageId}}", "$type": "image", "ObjectKey": "k", "ContentType": "image/png"}]""";

        var blocks = ChatContentJson.Deserialize(jsonb);

        Assert.Equal("hi", Assert.IsType<TextContentBlock>(blocks[0]).Text);
        Assert.Equal(imageId, Assert.IsType<ImageContentBlock>(blocks[1]).ImageId);
    }

    [Fact]
    public void Content_json_stored_before_ImageId_existed_still_deserializes()
    {
        const string legacy = """[{"$type":"image","ObjectKey":"boards/a.png","ContentType":"image/png"}]""";

        var image = Assert.IsType<ImageContentBlock>(Assert.Single(ChatContentJson.Deserialize(legacy)));

        Assert.Null(image.ImageId);
    }
}
