using SaigonWaterbus.Domain.Common;
using NUnit.Framework;
using Shouldly;

namespace SaigonWaterbus.Domain.UnitTests.Common;

public class BaseEntityTests
{
    [Test]
    public void AddDomainEventAddsEventToCollection()
    {
        var entity = new TestEntity();
        var domainEvent = new TestEvent();

        entity.AddDomainEvent(domainEvent);

        entity.DomainEvents.Count.ShouldBe(1);
        entity.DomainEvents.ShouldContain(domainEvent);
    }

    [Test]
    public void RemoveDomainEventRemovesEventFromCollection()
    {
        var entity = new TestEntity();
        var domainEvent = new TestEvent();
        entity.AddDomainEvent(domainEvent);

        entity.RemoveDomainEvent(domainEvent);

        entity.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void ClearDomainEventsRemovesAllEvents()
    {
        var entity = new TestEntity();
        entity.AddDomainEvent(new TestEvent());
        entity.AddDomainEvent(new TestEvent());

        entity.ClearDomainEvents();

        entity.DomainEvents.ShouldBeEmpty();
    }

    private sealed class TestEntity : BaseEntity
    {
    }

    private sealed class TestEvent : BaseEvent
    {
    }
}
