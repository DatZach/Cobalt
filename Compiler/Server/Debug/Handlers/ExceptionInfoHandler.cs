using Compiler.Server.Debug.Services;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Compiler.Server.Debug.Handlers
{
    internal sealed class ExceptionInfoHandler : ExceptionInfoHandlerBase
    {
        private readonly DebuggerService debuggerService;

        public ExceptionInfoHandler(DebuggerService debuggerService)
        {
            this.debuggerService = debuggerService;
        }

        public override Task<ExceptionInfoResponse> Handle(ExceptionInfoArguments request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ExceptionInfoResponse
            {
                ExceptionId = "0", // TODO ???
                Description = debuggerService.LastError_Name,
                BreakMode = ExceptionBreakMode.Unhandled,
                Details = new ExceptionDetails
                {
                    Message = debuggerService.LastError_NativeException?.Message ?? debuggerService.LastError_Name,
                    TypeName = debuggerService.LastError_NativeException?.GetType().Name ?? debuggerService.LastError_Name,
                    EvaluateName = "$error",
                    FullTypeName = debuggerService.LastError_NativeException?.GetType().FullName ?? $"error.{debuggerService.LastError_Name}",
                    StackTrace = debuggerService.LastError_StackTrace
                }
            });
        }
    }

    internal sealed class SetExceptionBreakpointsHandler : SetExceptionBreakpointsHandlerBase
    {
        private readonly DebuggerService debuggerService;

        public SetExceptionBreakpointsHandler(DebuggerService debuggerService)
        {
            this.debuggerService = debuggerService;
        }

        public override Task<SetExceptionBreakpointsResponse> Handle(SetExceptionBreakpointsArguments request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new SetExceptionBreakpointsResponse());
        }
    }
}