using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

/// <summary>
/// Responsibility: immutable, observed diary values for presentation; never owns learning records.
/// Lifetime/Owner: one visible page / AIDiaryUI. Maximum: 1000 events, 10000 characters per event.
/// Cleanup: replace on revision/page changes, release when closed. Failure: caller closes only the overlay.
/// </summary>
public sealed class AIDiarySnapshot
{
    public readonly long Revision;
    public readonly Team Faction;
    public readonly string ReportText;
    public readonly int TotalEvents, PageIndex, PageCount;
    public readonly ReadOnlyCollection<AIDiaryEvent> Events;

    public AIDiarySnapshot(long revision, Team faction, string reportText,
        IList<AIActionRecord> records, int pageIndex, int maximumEvents)
    {
        Revision = revision; Faction = faction; ReportText = reportText ?? "";
        maximumEvents = Math.Max(1, Math.Min(1000, maximumEvents));
        TotalEvents = records?.Count ?? 0;
        PageCount = Math.Max(1, (TotalEvents + maximumEvents - 1) / maximumEvents);
        PageIndex = Math.Max(0, Math.Min(PageCount - 1, pageIndex));
        // Page zero is newest; values on each page retain their actual execution order.
        int end = Math.Max(0, TotalEvents - PageIndex * maximumEvents);
        int start = Math.Max(0, end - maximumEvents);
        var values = new AIDiaryEvent[end - start];
        for (int i = start; i < end; i++) values[i - start] = new AIDiaryEvent(records[i]);
        Events = Array.AsReadOnly(values);
    }

    /// <summary>Render only this bounded page. Neither opening nor rendering reads a file.</summary>
    public string RenderEvents()
    {
        var builder = new StringBuilder(2048);
        for (int i = 0; i < Events.Count; i++)
            builder.Append(Events[i].Render());
        return builder.Length == 0 ? "このページに記録されたAI行動はまだありません。" : builder.ToString();
    }
}

/// <summary>Value-only copy; UI cannot alter a reward, action, operation or hidden enemy state.</summary>
public readonly struct AIDiaryEvent
{
    public readonly long ActionId;
    public readonly int Turn;
    public readonly AIActionType ActionType;
    public readonly string ActorRole, Intent, ExpectedOutcome, ActualOutcome, Lesson;
    public readonly float BaseScore, LearnedModifier, FinalScore, Reward;
    public readonly AIFailureReason FailureReason;
    public readonly bool ExecutionSucceeded;

    public AIDiaryEvent(AIActionRecord record)
    {
        var trace = record?.DecisionTrace;
        ActionId = record?.ActionId ?? 0; Turn = record?.Turn ?? 0;
        ActionType = record?.ActionType ?? default; ActorRole = Limited(record?.ActorRole, 128);
        Intent = Limited(trace?.Intent, 2000); ExpectedOutcome = Limited(trace?.ExpectedOutcome, 2000);
        ActualOutcome = Limited(trace?.ActualOutcome, 2000); Lesson = Limited(trace?.Lesson, 2000);
        BaseScore = record?.BaseScore ?? 0; LearnedModifier = record?.LearnedModifier ?? 0;
        FinalScore = record?.FinalScore ?? 0; Reward = record?.Reward ?? 0;
        FailureReason = record?.FailureReason ?? default; ExecutionSucceeded = record?.ExecutionSucceeded ?? false;
    }

    public string Render()
    {
        var builder = new StringBuilder(512);
        builder.Append("Turn ").Append(Turn).Append("　").Append(ActorRole).Append("　").Append(ActionType)
            .Append("　行動ID ").Append(ActionId).AppendLine();
        builder.Append("通常評価 ").Append(BaseScore.ToString("F2")).Append("　経験補正 ")
            .Append(LearnedModifier.ToString("F2")).Append("　最終評価 ").Append(FinalScore.ToString("F2"))
            .Append("　報酬 ").Append(Reward.ToString("F2")).AppendLine();
        if (!string.IsNullOrEmpty(Intent)) builder.Append("意図: ").AppendLine(Intent);
        if (!string.IsNullOrEmpty(ExpectedOutcome)) builder.Append("期待: ").AppendLine(ExpectedOutcome);
        builder.Append("結果: ").Append(ExecutionSucceeded ? "実行成功" : "実行失敗");
        if (FailureReason != AIFailureReason.None) builder.Append(" / ").Append(FailureReason);
        builder.AppendLine();
        if (!string.IsNullOrEmpty(ActualOutcome)) builder.AppendLine(ActualOutcome);
        if (!string.IsNullOrEmpty(Lesson)) builder.Append("学習: ").AppendLine(Lesson);
        return builder.AppendLine().ToString();
    }

    static string Limited(string text, int maximum)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (text.Length <= maximum) return text;
        int length = maximum;
        if (char.IsHighSurrogate(text[length - 1])) length--;
        return text.Substring(0, length);
    }
}
