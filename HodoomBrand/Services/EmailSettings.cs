namespace HodoomBrand.Services;

public class EmailSettings
{
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    public string AppPassword { get; set; } = "";
    public string FromName { get; set; } = "BORDO";
}