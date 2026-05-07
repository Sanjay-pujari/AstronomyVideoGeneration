namespace Astronomy.MediaFactory.Contracts;

public sealed class MetaOptions
{
    public const string SectionName = "Meta";

    public string AppId { get; set; } = "";
    public string AppSecret { get; set; } = "";
    public string RedirectUri { get; set; } = "https://localhost:59235/api/metaoauth/callback";
    public string ExpectedFacebookPageName { get; set; } = "";
    public string ExpectedFacebookPageId { get; set; } = "";
    public string ExpectedInstagramUsername { get; set; } = "";
    public bool PublishFacebook { get; set; } = true;
    public bool PublishInstagram { get; set; } = true;
    public string? TokenFilePath { get; set; }
    public List<string> Scopes { get; set; } =
    [
        "pages_manage_posts",
        "pages_read_engagement",
        "pages_show_list",
        "instagram_basic",
        "instagram_content_publish",
        "business_management"
    ];
}
