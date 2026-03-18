namespace Aira.Application.Abstractions;

public interface IReportChatAvailabilityService
{
    bool IsConfigured { get; }

    string UnavailableReason { get; }
}
