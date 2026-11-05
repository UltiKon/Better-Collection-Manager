using System;
using System.Collections.Generic;

public static class Navigation
{
    public delegate bool NavigateBackHandler();
    private static readonly Stack<NavigateBackHandler> History = new Stack<NavigateBackHandler>();
    public static bool Enabled = true;
    public static bool CanGoBack => Enabled && History.Count > 0;
    public static int Count => History.Count;
    public static void Clear() { History.Clear(); Enabled = true; }
    public static void Push(NavigateBackHandler handler) => History.Push(handler);
    public static bool GoBack()
    {
        if (!CanGoBack) return false;
        NavigateBackHandler handler = History.Peek();
        if (!handler()) return false;
        if (History.Count > 0 && History.Peek() == handler) History.Pop();
        return true;
    }
}

public sealed class CraftingPendingTransaction
{
    public enum Operation { Invalid, NormalDisenchant, GoldenDisenchant, DiamondDisenchant, SignatureDisenchant, NormalCreate }
    public string CardID;
    public readonly List<Operation> Operations = new List<Operation>();
    public void Add(Operation operation) => Operations.Add(operation);
    public bool GetLastTransactionWasCrafting() => Operations.Count > 0 && Operations[Operations.Count - 1] == Operation.NormalCreate;
    public int GetTransactionAmount(TAG_PREMIUM premium)
    {
        Operation sale = premium == TAG_PREMIUM.NORMAL ? Operation.NormalDisenchant : premium == TAG_PREMIUM.GOLDEN ? Operation.GoldenDisenchant
            : premium == TAG_PREMIUM.DIAMOND ? Operation.DiamondDisenchant : Operation.SignatureDisenchant;
        return -Operations.FindAll(value => value == sale).Count + (premium == TAG_PREMIUM.NORMAL ? Operations.FindAll(value => value == Operation.NormalCreate).Count : 0);
    }
}
public enum Option { HAS_DISENCHANTED }
public sealed class Options
{
    public static Options Get() => new Options();
    public void SetBool(Option option, bool value) { }
}
public sealed class AlertPopup
{
    public enum Response { CONFIRM, CANCEL }
    public enum ResponseDisplay { CONFIRM_CANCEL }
    public sealed class PopupInfo
    {
        public string m_headerText, m_text;
        public bool m_showAlertIcon;
        public ResponseDisplay m_responseDisplay;
        public Action<Response, object> m_responseCallback;
    }
}

namespace Blizzard.GameService.SDK.Client.Integration
{
    public sealed class BnetGameAccountId
    {
        public ulong High, Low;
        public bool IsValid() => Low != 0;
    }
    public static class BattleNet
    {
        public static BnetGameAccountId Account;
        public static BnetGameAccountId GetMyGameAccountId() => Account;
    }
}
