using MassTransit;

namespace WiSave.Portal.Contracts.Bus;

/// <summary>
/// Marker interface for the portal's message bus, used to send messages to the portal's consumers.
/// </summary>
public interface IPortalBus : IBus;