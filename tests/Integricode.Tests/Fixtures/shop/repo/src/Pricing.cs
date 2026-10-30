namespace Shop;

public static class Pricing
{
    public static decimal Discount(decimal total, decimal percent) => total * percent / 100;
}
