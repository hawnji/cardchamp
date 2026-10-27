namespace CardVault.Core.Common
{
    /// <summary>
    /// Stable error codes returned by domain operations. UI maps these to player-facing text.
    /// </summary>
    public enum OpError
    {
        None = 0,
        AlreadyProcessed,
        NotFound,
        NotOwned,
        InsufficientFunds,
        InsufficientTickets,
        InsufficientPoints,
        OutOfStock,
        NotAvailable,
        InvalidState,
        Locked,
        CardBusy,
        NotEligible,
        InvalidArgument,
        AlreadyClaimed,
        NotTradable,
    }

    public readonly struct OpResult
    {
        public readonly OpError Error;
        public readonly string Message;

        private OpResult(OpError error, string message)
        {
            Error = error;
            Message = message;
        }

        public bool Ok => Error == OpError.None;

        /// <summary>True when the request was a replay of an already-committed transaction.</summary>
        public bool WasDuplicate => Error == OpError.AlreadyProcessed;

        public static OpResult Success() => new OpResult(OpError.None, null);
        public static OpResult Fail(OpError error, string message) => new OpResult(error, message);

        public override string ToString() => Ok ? "Ok" : Error + ": " + Message;
    }

    public readonly struct OpResult<T>
    {
        public readonly OpError Error;
        public readonly string Message;
        public readonly T Value;

        private OpResult(OpError error, string message, T value)
        {
            Error = error;
            Message = message;
            Value = value;
        }

        public bool Ok => Error == OpError.None;
        public bool WasDuplicate => Error == OpError.AlreadyProcessed;

        public static OpResult<T> Success(T value) => new OpResult<T>(OpError.None, null, value);
        public static OpResult<T> Fail(OpError error, string message) => new OpResult<T>(error, message, default);

        public OpResult AsResult() => Ok ? OpResult.Success() : OpResult.Fail(Error, Message);

        public override string ToString() => Ok ? "Ok(" + Value + ")" : Error + ": " + Message;
    }
}
