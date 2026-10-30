using Brasa.Domain;
using Xunit;
namespace Brasa.Tests;

public sealed class DomainTests
{
    [Fact]
    public void Prices_balance_with_discount_and_commission()
    {
        var p = PriceBreakdown.Calculate(59.90m, 8, 7, 2.5m, .12m);
        Assert.Equal(61.40m, p.Total);
        Assert.Equal(p.Total, p.RestaurantAmount + p.CourierAmount + p.Commission + p.ServiceFee);
        Assert.Equal(6.23m, p.Commission);
    }
    [Theory][InlineData(-1, 0)][InlineData(10, 11)][InlineData(0, 0)] public void Invalid_money_is_rejected(int subtotal, int discount) => Assert.Throws<RuleException>(() => PriceBreakdown.Calculate(subtotal, discount, 7, 2, .12m));
    [Fact]
    public void State_machine_rejects_skips_duplicates_and_terminal_changes()
    {
        var o = new Order();
        Assert.Throws<RuleException>(() => o.Transition(OrderStatus.Delivered, "user"));
        o.Transition(OrderStatus.SentToRestaurant, "payment");
        Assert.Throws<RuleException>(() => o.Transition(OrderStatus.SentToRestaurant, "payment"));
        Assert.Throws<RuleException>(() => o.Transition(OrderStatus.Cancelled, "user"));
        o.Transition(OrderStatus.Cancelled, "user", "Desistência");
        Assert.Throws<RuleException>(() => o.Transition(OrderStatus.Accepted, "store"));
        Assert.Equal(2, o.Events.Count);
        Assert.Equal("SentToRestaurant", o.Events[1].Previous);
    }
    [Fact]
    public void Delivery_requires_each_step()
    {
        var d = new Delivery();
        Assert.Throws<RuleException>(() => d.Advance(DeliveryStage.Delivered));
        foreach (var s in new[] { DeliveryStage.GoingToRestaurant, DeliveryStage.AtRestaurant, DeliveryStage.PickedUp, DeliveryStage.GoingToCustomer, DeliveryStage.Delivered })
            d.Advance(s);
        Assert.Throws<RuleException>(() => d.Advance(DeliveryStage.Problem));
    }
}
