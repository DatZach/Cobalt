using Compiler.Server.Debug.Services;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Compiler.Server.Debug.Handlers
{
    internal sealed class LaunchHandler : LaunchHandlerBase
    {
        private readonly DebuggerService debuggerService;

        public LaunchHandler(DebuggerService debuggerService)
        {
            this.debuggerService = debuggerService;
        }

        public override Task<LaunchResponse> Handle(LaunchRequestArguments request, CancellationToken cancellationToken)
        {
            debuggerService.Start();
            return Task.FromResult(new LaunchResponse());
        }
    }

    internal sealed class TerminateHandler : TerminateHandlerBase
    {
        private readonly DebuggerService debuggerService;

        public TerminateHandler(DebuggerService debuggerService)
        {
            this.debuggerService = debuggerService;
        }

        public override Task<TerminateResponse> Handle(TerminateArguments request, CancellationToken cancellationToken)
        {
            debuggerService.Stop();
            return Task.FromResult(new TerminateResponse());
        }
    }

    internal sealed class RestartHandler : RestartHandlerBase
    {
        private readonly DebuggerService debuggerService;

        public RestartHandler(DebuggerService debuggerService)
        {
            this.debuggerService = debuggerService;
        }

        public override Task<RestartResponse> Handle(RestartArguments request, CancellationToken cancellationToken)
        {
            debuggerService.Stop();
            debuggerService.Start();
            return Task.FromResult(new RestartResponse());
        }
    }

    internal sealed class PauseHandler : PauseHandlerBase
    {
        private readonly DebuggerService debuggerService;

        public PauseHandler(DebuggerService debuggerService)
        {
            this.debuggerService = debuggerService;
        }

        public override Task<PauseResponse> Handle(PauseArguments request, CancellationToken cancellationToken)
        {
            debuggerService.Pause();
            return Task.FromResult(new PauseResponse());
        }
    }

    internal sealed class ContinueHandler : ContinueHandlerBase
    {
        private readonly DebuggerService debuggerService;

        public ContinueHandler(DebuggerService debuggerService)
        {
            this.debuggerService = debuggerService;
        }

        public override Task<ContinueResponse> Handle(ContinueArguments request, CancellationToken cancellationToken)
        {
            debuggerService.Start();
            return Task.FromResult(new ContinueResponse { AllThreadsContinued = true });
        }
    }

    internal sealed class NextHandler : NextHandlerBase
    {
        private readonly DebuggerService debuggerService;

        public NextHandler(DebuggerService debuggerService)
        {
            this.debuggerService = debuggerService;
        }

        public override Task<NextResponse> Handle(NextArguments request, CancellationToken cancellationToken)
        {
            // TODO Support granularity
            debuggerService.StepOver();
            return Task.FromResult(new NextResponse());
        }
    }

    internal sealed class StepInTargetsHandler : StepInTargetsHandlerBase
    {
        private readonly DebuggerService debuggerService;

        public StepInTargetsHandler(DebuggerService debuggerService)
        {
            this.debuggerService = debuggerService;
        }

        public override Task<StepInTargetsResponse> Handle(StepInTargetsArguments request, CancellationToken cancellationToken)
        {
            // TODO Implement
            return Task.FromResult(new StepInTargetsResponse
            {
                Targets = null
                //Targets = new Container<StepInTarget>(
                //    new StepInTarget { Id = 0, Label = "Add" },
                //    new StepInTarget { Id = 1, Label = "string" }
                //)
            });
        }
    }

    internal sealed class StepInHandler : StepInHandlerBase
    {
        private readonly DebuggerService debuggerService;

        public StepInHandler(DebuggerService debuggerService)
        {
            this.debuggerService = debuggerService;
        }

        public override Task<StepInResponse> Handle(StepInArguments request, CancellationToken cancellationToken)
        {
            debuggerService.StepIn();
            return Task.FromResult(new StepInResponse());
        }
    }

    internal sealed class StepOutHandler : StepOutHandlerBase
    {
        private readonly DebuggerService debuggerService;

        public StepOutHandler(DebuggerService debuggerService)
        {
            this.debuggerService = debuggerService;
        }

        public override Task<StepOutResponse> Handle(StepOutArguments request, CancellationToken cancellationToken)
        {
            debuggerService.StepOut();
            return Task.FromResult(new StepOutResponse());
        }
    }
    
    internal sealed class GotoTargetsHandler : GotoTargetsHandlerBase
    {
        private readonly DebuggerService debuggerService;

        public GotoTargetsHandler(DebuggerService debuggerService)
        {
            this.debuggerService = debuggerService;
        }

        public override Task<GotoTargetsResponse> Handle(GotoTargetsArguments request, CancellationToken cancellationToken)
        {
            //request.Source, request.Line
            return Task.FromResult(new GotoTargetsResponse
            {
                Targets = new Container<GotoTarget>(
                    new GotoTarget
                    {
                        Id = 0,
                        Label = "Line 31",
                        Line = 31,
                        InstructionPointerReference = "0"
                    },
                    new GotoTarget
                    {
                        Id = 1,
                        Label = "Line 63",
                        Line = 63,
                        InstructionPointerReference = "25"
                    }
                )
            });
        }
    }

    internal sealed class GotoHandler : GotoHandlerBase
    {
        private readonly DebuggerService debuggerService;

        public GotoHandler(DebuggerService debuggerService)
        {
            this.debuggerService = debuggerService;
        }

        public override Task<GotoResponse> Handle(GotoArguments request, CancellationToken cancellationToken)
        {
            debuggerService.GoTo(request.TargetId);

            return Task.FromResult(new GotoResponse());
        }
    }
}
