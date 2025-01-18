#pragma warning disable SKEXP0080
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;

/*
"""
AI餐饮助手
系统协作流程
开始：获取用户背景信息，用户说出自己的需求
1. 营养规划师首先根据用户信息生成初始餐饮计划 
2. 健康指导老师进行评估和审核： - 如果计划合适：确认并输出最终方案 - 如果需要调整：提供具体修改建议 
3. 营养规划师根据建议修改计划（如需要） 
4. 最终输出经过双重确认的个性化餐饮方案
""";
*/
Kernel CreateKernelWithChatCompletion()
{
    Kernel kernel = Kernel.CreateBuilder()
        .AddOpenAIChatCompletion(
            modelId: "gpt-4o",
            apiKey: File.ReadAllText("c:/gpt/key.txt"))
        .Build();
    return kernel;
}


var process = new ProcessBuilder("AICateringAssistant");
var getNutritionalPlannerProcessStep = process.AddStepFromType<NutritionalPlannerProcessStep>();
var getHealthGuidanceTeacherProcessStep = process.AddStepFromType<HealthGuidanceTeacherProcessStep>();
var getCompletedStep = process.AddStepFromType<CompletedProcessStep>();

// 开始事件，传递 ProcessData  
process.OnInputEvent(AICateringEvents.StartProcess)
    .SendEventTo(new ProcessFunctionTargetBuilder(
        getNutritionalPlannerProcessStep,
        NutritionalPlannerProcessStep.Functions.NutritionalPlanner));

// 营养规划师完成后，发送到健康指导老师  
getNutritionalPlannerProcessStep
    .OnEvent(AICateringEvents.HealthGuidanceTeacher)
    .SendEventTo(new ProcessFunctionTargetBuilder(
        getHealthGuidanceTeacherProcessStep,
        HealthGuidanceTeacherProcessStep.Functions.HealthGuidanceTeacher));

// 健康指导老师反馈  
getHealthGuidanceTeacherProcessStep
    .OnEvent(AICateringEvents.FeedbackProvided)
    .SendEventTo(new ProcessFunctionTargetBuilder(
        getNutritionalPlannerProcessStep,
        NutritionalPlannerProcessStep.Functions.NutritionalPlanner));

// 健康指导老师满意，流程完成  
getHealthGuidanceTeacherProcessStep
    .OnEvent(AICateringEvents.AICateringAssistantCompleted)
    .SendEventTo(new ProcessFunctionTargetBuilder(
        getCompletedStep,
        CompletedProcessStep.Functions.CompletedProcess));

// 在完成步骤中，停止流程  
getCompletedStep
    .OnEvent(AICateringEvents.ProcessCompleted)
    .StopProcess();

var kernelProcess = process.Build();
Kernel kernel = CreateKernelWithChatCompletion();

while (true)
{
    Console.WriteLine("个人信息:");
    //var userContent = Console.ReadLine();  
    var userContent = """  
                       姓名：李华  
                       年龄：28岁  
                       性别：女  
                       身高：165厘米  
                       体重：55公斤  
                       职业：软件工程师  
                       饮食偏好：  
                       - 喜爱日式料理和素食  
                       - 忌口辣椒和乳制品  
                       健康状况：乳糖不耐受，正在控制体重  
                       运动习惯：每周跑步三次，每次30分钟  
  
                       """;
    Console.WriteLine(userContent);
    Console.WriteLine("请输入个人需求:");
    var userRequire = Console.ReadLine();

    // 创建 ProcessData 实例  
    var processData = new ProcessData
    {
        UserInfo = userContent,
        UserRequire = userRequire,
        Feedback = null,
        LastPlan = null
    };

    // 启动流程，传递 processData  
    using var runningProcess = await kernelProcess.StartAsync(kernel, new KernelProcessEvent() { Id = AICateringEvents.StartProcess, Data = processData });
}

//营养规划师
public class NutritionalPlannerProcessStep : KernelProcessStep
{
    public static class Functions
    {
        public const string NutritionalPlanner = nameof(NutritionalPlanner);
    }

    [KernelFunction(Functions.NutritionalPlanner)]
    public async Task NutritionalPlannerAsync(KernelProcessStepContext context, ProcessData processData, Kernel _kernel)
    {
        processData.IterationCount++;

        var chat = _kernel.GetRequiredService<IChatCompletionService>();
        var chatHistory = new ChatHistory();

        var userInfo = processData.UserInfo;
        var userRequire = processData.UserRequire;
        var feedback = processData.Feedback;

        chatHistory.AddSystemMessage(
            """  
            你是一位专业的营养规划师，负责为用户制定个性化的餐饮计划。你需要：  
  
            1. 严格遵循以下原则：  
            - 确保所有建议符合用户的饮食限制和过敏情况  
            - 计算并平衡每餐的营养成分  
            - 考虑用户的口味偏好和生活方式  
            - 提供实用且易于准备的餐食选择  
  
            2. 输出格式要求：  
            - 为每餐提供详细的菜品描述  
            - 列出具体的配料和份量  
            - 提供每餐的营养成分分析（热量、蛋白质、碳水化合物等）  
            - 包含适量的零食建议  
  
            3. 在制定计划时，你要：  
            - 优先考虑用户的饮食禁忌  
            - 根据用户的运动习惯和健康指导老师的反馈调整营养分配  
            - 确保食材新鲜且容易获得  
            - 提供可替代的食材选项  
  
            请基于用户信息和健康指导老师的反馈生成符合要求的详细餐饮计划。  
            """);

        // 添加用户信息和需求  
        chatHistory.AddUserMessage($"{userInfo}\n{userRequire}");

        // 如果有反馈，添加到对话历史  
        if (!string.IsNullOrEmpty(feedback))
        {
            chatHistory.AddAssistantMessage(processData.LastPlan ?? "");
            chatHistory.AddUserMessage($"健康指导老师的反馈：{feedback}");
        }

        var chatResult = chat.GetStreamingChatMessageContentsAsync(chatHistory);
        Console.ForegroundColor = ConsoleColor.Green;
        await Console.Out.WriteLineAsync($"\n营养规划师（第 {processData.IterationCount} 次迭代）:");
        await Console.Out.WriteLineAsync("\n营养规划师:");
        var planSb = new StringBuilder();
        await foreach (var item in chatResult)
        {
            Console.Write(item);
            planSb.Append(item.Content ?? "");
        }
        Console.ResetColor();

        // 保存本次生成的计划到 processData  
        processData.LastPlan = planSb.ToString();

        // 清除反馈  
        processData.Feedback = null;

        // 发送事件到健康指导老师，携带当前的 processData  
        await context.EmitEventAsync(new() { Id = AICateringEvents.HealthGuidanceTeacher, Data = processData, Visibility = KernelProcessEventVisibility.Public });
    }
}

/// <summary>
///健康指导老师步骤
/// </summary>
public class HealthGuidanceTeacherProcessStep : KernelProcessStep
{
    public static class Functions
    {
        public const string HealthGuidanceTeacher = nameof(HealthGuidanceTeacher);
    }

    [KernelFunction(Functions.HealthGuidanceTeacher)]
    public async Task HealthGuidanceTeacherAsync(KernelProcessStepContext context, ProcessData processData, Kernel _kernel)
    {
        // 检查迭代次数  
        if (processData.IterationCount >= 5)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n迭代次数已达上限，流程自动结束。");
            Console.ResetColor();

            // 触发流程完成事件  
            await context.EmitEventAsync(new() { Id = AICateringEvents.AICateringAssistantCompleted, Data = processData, Visibility = KernelProcessEventVisibility.Public });
            return;
        }

        var chat = _kernel.GetRequiredService<IChatCompletionService>();
        var chatHistory = new ChatHistory();

        var plan = processData.LastPlan;

        chatHistory.AddSystemMessage(
            """  
            你是一位专业的健康指导老师，负责监督和评估营养规划师制定的餐饮计划。你的职责是：  
  
            1. 审查标准：  
            - 验证营养摄入是否平衡且适合用户需求  
            - 确保计划符合用户的健康状况和限制  
            - 评估餐食搭配的科学性和可行性  
            - 检查是否符合用户的运动强度需求  
  
            2. 评估重点：  
            - 总热量是否符合用户的体重控制目标  
            - 营养素比例是否合理  
            - 餐食间隔和分配是否恰当  
            - 零食选择是否健康且适量  
  
            3. 如发现问题，需要：  
            - 指出具体的问题点  
            - 提供修改建议  
            - 解释修改的理由和依据  
            - 确保修改后的方案仍符合用户偏好  
  
            4. 评分与反馈：  
            - 根据餐饮计划的质量，在 1 至 10 分范围内给出评分：  
              - 10分：表示内容极其优秀，无明显改进空间。  
              - 区间分数：解释得分依据，并逐步优化建议。  
              - 若为满分，给予表扬与激励，流程结束。
              - 打分输出格式： 1分、2分...5分、6分...9分、10分
  
            请对营养规划师的计划进行专业评估并提供改进建议。  
            """);

        chatHistory.AddAssistantMessage(plan);

        var chatResult = chat.GetStreamingChatMessageContentsAsync(chatHistory);
        await Console.Out.WriteLineAsync();
        Console.ForegroundColor = ConsoleColor.Yellow;
        await Console.Out.WriteLineAsync($"\n健康指导老师（第 {processData.IterationCount} 次迭代）:");
        await Console.Out.WriteLineAsync("\n健康指导老师:");
        var feedbackSb = new StringBuilder();
        await foreach (var item in chatResult)
        {
            Console.Write(item);
            feedbackSb.Append(item.Content ?? "");
        }
        Console.ResetColor();

        // 获取反馈内容  
        var feedbackStr = feedbackSb.ToString();

        // 判断是否满意  
        if (feedbackStr.Contains("6分") || feedbackStr.Contains("7分") || feedbackStr.Contains("8分") || feedbackStr.Contains("9分") || feedbackStr.Contains("10分"))
        {
            // 满意，设置反馈为 null  
            processData.Feedback = null;

            // 发送事件，流程完成  
            await context.EmitEventAsync(new() { Id = AICateringEvents.AICateringAssistantCompleted, Data = processData, Visibility = KernelProcessEventVisibility.Public });
        }
        else
        {
            // 不满意，保存反馈  
            processData.Feedback = feedbackStr;

            // 发送反馈事件  
            await context.EmitEventAsync(new() { Id = AICateringEvents.FeedbackProvided, Data = processData, Visibility = KernelProcessEventVisibility.Public });
        }
    }
}
/// <summary>
/// 完成步骤
/// </summary>
public class CompletedProcessStep : KernelProcessStep
{
    public static class Functions
    {
        public const string CompletedProcess = nameof(CompletedProcess);
    }

    [KernelFunction(Functions.CompletedProcess)]
    public async Task CompleteAsync(KernelProcessStepContext context, ProcessData processData, Kernel _kernel)
    {
        await Console.Out.WriteLineAsync();
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine("流程结束，最终的餐饮计划如下：");
        Console.ResetColor();
        Console.WriteLine(processData.LastPlan);

        // 触发流程完成事件，以停止流程  
        await context.EmitEventAsync(new() { Id = AICateringEvents.ProcessCompleted, Data = null, Visibility = KernelProcessEventVisibility.Public });
    }
}
public static class AICateringEvents
{
    // 开始 Process  
    public static readonly string StartProcess = nameof(StartProcess);
    /// <summary>  
    /// 健康指导老师  
    /// </summary>  
    public static readonly string HealthGuidanceTeacher = nameof(HealthGuidanceTeacher);
    /// <summary>  
    /// AI餐饮助手完成  
    /// </summary>  
    public static readonly string AICateringAssistantCompleted = nameof(AICateringAssistantCompleted);
    /// <summary>  
    /// 健康指导老师的反馈  
    /// </summary>  
    public static readonly string FeedbackProvided = nameof(FeedbackProvided);
    /// <summary>  
    /// 流程完成  
    /// </summary>  
    public static readonly string ProcessCompleted = nameof(ProcessCompleted);
}

/// <summary>  
/// 用于在流程中传递数据的类  
/// </summary>  
public class ProcessData
{
    public string UserInfo { get; set; }
    public string UserRequire { get; set; }
    public string Feedback { get; set; }
    public string LastPlan { get; set; }
    public int IterationCount { get; set; }
}