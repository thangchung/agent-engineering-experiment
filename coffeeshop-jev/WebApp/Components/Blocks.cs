using Microsoft.AspNetCore.Components.AI;
using WebApp.Models;

namespace WebApp.Components;

[ToolBlock("show_menu")]
public partial class ShowMenuBlock : FunctionInvocationContentBlock
{
    [ToolResult]
    public List<MenuEntry>? Items { get; set; }
}

[ToolBlock("show_history")]
public partial class ShowHistoryBlock : FunctionInvocationContentBlock
{
    [ToolResult]
    public List<OrderRecordInfo>? Orders { get; set; }
}
