using NUnit.Framework;
using SaigonWaterbus.Domain.Common;
using SaigonWaterbus.Domain.Entities;
using Shouldly;

namespace SaigonWaterbus.Domain.UnitTests.Common;

public class BaseEntityTests
{
    [Test]
    public void BaseEntityGeneratesGuidIdWhenCreated()
    {
        var entity = new TestEntity();

        entity.Id.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public void VesselGeneratesGuidIdWhenCreated()
    {
        var vessel = new Vessel();

        vessel.Id.ShouldNotBe(Guid.Empty);
    }

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
