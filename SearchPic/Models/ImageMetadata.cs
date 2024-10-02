namespace SearchPic.Models
{
    public class ImageMetadata
    {
        public string Id { get; set; }
        public string ImageUrl { get; set; }
        public string Description { get; set; }
        public List<string> Keywords { get; set; }
    }
}