using PcDashboard.Core;

namespace PcDashboard.Desktop;

public sealed record OperationViewState(string Subject, string Verb,
    ActionProgress? Progress, ActionResult? Result, Action? Cancel);
