// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEngine.UIElements;

namespace Unity.UIToolkit.Editor;

class GetCanvasThemeQuery : Command<GetCanvasThemeQuery>
{
    public static ThemeStyleSheet Get()
    {
        var result = default(ThemeStyleSheet);
        UICommandQueue.RegisterHandler<QueryPayload>(Handler);
        try
        {
            using var command = GetPooled();
            UICommandQueue.Execute(command);
        }
        finally
        {
            UICommandQueue.UnregisterHandler<QueryPayload>(Handler);
        }
        return result;

        void Handler(in CommandContext context)
        {
            result = ((QueryPayload)context.Command).Payload;
        }
    }

    public class QueryPayload : Command<QueryPayload>
    {
        public static QueryPayload GetPooled(object source, ThemeStyleSheet theme)
        {
            var cmd = GetPooled();
            cmd.Source = source;
            cmd.Payload = theme;
            return cmd;
        }

        public static void Execute(object source, ThemeStyleSheet theme)
        {
            using var command = GetPooled(source, theme);
            UICommandQueue.Execute(command);
        }

        public ThemeStyleSheet Payload { get; private set; }

        protected override void Init()
        {
            base.Init();
            Payload = null;
        }

        public override bool Validate() => true;

        public override CommandExecutionStatus Execute() => Payload
            ? CommandExecutionStatus.Success
            : CommandExecutionStatus.ExecutionFailed;
    }

    public override bool Validate() => true;
    public override CommandExecutionStatus Execute() => CommandExecutionStatus.Success;
}
