using System.Text.Json;
using HodoomBrand.Models;

namespace HodoomBrand.Helpers;

public static class CartSession
{
    private const string SessionKey = "cart";

    public static List<CartItem> Get(ISession session)
    {
        var json = session.GetString(SessionKey);
        return string.IsNullOrEmpty(json)
            ? new List<CartItem>()
            : JsonSerializer.Deserialize<List<CartItem>>(json) ?? new List<CartItem>();
    }

    public static void Save(ISession session, List<CartItem> cart)
        => session.SetString(SessionKey, JsonSerializer.Serialize(cart));

    public static void Clear(ISession session) => session.Remove(SessionKey);
}