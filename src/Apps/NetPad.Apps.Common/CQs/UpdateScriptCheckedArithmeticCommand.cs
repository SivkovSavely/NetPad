using MediatR;
using NetPad.Events;
using NetPad.Scripts;
using NetPad.Scripts.Events;

namespace NetPad.Apps.CQs;

public class UpdateScriptCheckedArithmeticCommand(Script script, bool checkedArithmetic) : Command
{
    public Script Script { get; } = script;
    public bool CheckedArithmetic { get; } = checkedArithmetic;

    public class Handler(IEventBus eventBus) : IRequestHandler<UpdateScriptCheckedArithmeticCommand>
    {
        public async Task<Unit> Handle(UpdateScriptCheckedArithmeticCommand request, CancellationToken cancellationToken)
        {
            var script = request.Script;
            var oldValue = script.Config.CheckedArithmetic;
            var newValue = request.CheckedArithmetic;
            if (oldValue == newValue)
            {
                return Unit.Value;
            }

            script.Config.SetCheckedArithmetic(newValue);
            await eventBus.PublishAsync(new ScriptCheckedArithmeticUpdatedEvent(script, oldValue, newValue));
            return Unit.Value;
        }
    }
}
