using ScumRconTool;
using ScumRconTool.Services;

var now = new DateTime(2026,10,3,12,0,0,DateTimeKind.Utc);
var settings = new BotSettings { ChallengeRotationEnabled = true, ChallengeRotationMaximum = 3, ChallengeRotationPauseHours = 24, ChallengeCompletedVisibleMinutes = 10 };
int checks = 0;
void Check(bool condition, string description) { if (!condition) throw new Exception(description); checks++; Console.WriteLine("PASS " + description); }
WeeklyCommunityTaskDefinition Task(string id, bool enabled = false) => new() { Id = id, Enabled = enabled, AutoRotate = true };
var manual = Task("manual", true); manual.AutoRotate = false;
var future = Task("reserved", true); future.StartUtc = now.AddDays(1).ToString("O");
var old = Task("old"); old.LastScheduledUtc = now.AddDays(-5);
var recent = Task("recent"); recent.LastScheduledUtc = now.AddDays(-2);
var list = new List<WeeklyCommunityTaskDefinition> { manual, future, old, recent };
Check(ChallengePlanningService.Maintain(settings,list,now,_ => false), "Rotation fills a free slot");
Check(list.Count(x => x.Enabled) == 3 && old.Enabled && !recent.Enabled, "Manual and future reservations count toward maximum; oldest template wins");
Check(WeeklyCommunityTaskService.Resets == 1 && old.CompletedRunUtc == null && old.EndUtc == "", "A new run resets its baseline and clears old end state");
Check(!ChallengePlanningService.Maintain(settings,list,now,_ => false), "Repeated maintenance does not restart active runs");
var finished = Task("finished",true); finished.CompletedRunUtc = now.AddMinutes(-9);
var personal = Task("personal",true); personal.GoalScope = "PerPlayer"; personal.CompletedRunUtc = now.AddHours(-1);
settings.ChallengeRotationEnabled = false;
list = new() { finished, personal };
ChallengePlanningService.Maintain(settings,list,now,_ => false);
Check(finished.Enabled && personal.Enabled, "Community visibility grace period and personal duration are respected");
ChallengePlanningService.Maintain(settings,list,now.AddMinutes(1),_ => false);
Check(!finished.Enabled && personal.Enabled && finished.LastRunEndedUtc == now.AddMinutes(1), "Completed community closes at configured deadline; personal stays active");
settings.ChallengeRotationEnabled = true;
list = new() { finished };
ChallengePlanningService.Maintain(settings,list,now.AddHours(1),_ => false);
Check(!finished.Enabled, "Repeat cooldown prevents daily repetition of the same template");
ChallengePlanningService.Maintain(settings,list,now.AddHours(25),_ => false);
Check(finished.Enabled && finished.CompletedRunUtc == null, "A template becomes eligible again after cooldown with clean progress");
var quiz = Task("quiz"); quiz.Type = "Quiz";
var excluded = Task("excluded"); excluded.AutoRotate = false;
list = new() { quiz, excluded };
Check(!ChallengePlanningService.Maintain(settings,list,now,_ => false), "Quiz and unselected templates are not auto scheduled");
var expired = Task("expired",true); list = new() { expired };
ChallengePlanningService.Maintain(settings,list,now,_ => true);
Check(!expired.Enabled, "Expired tasks retire and do not immediately repeat");
var restarted = Task("restart",true); restarted.CompletedRunUtc = now;
ChallengePlanningService.Schedule(restarted,now.AddDays(2));
Check(restarted.Enabled && restarted.CompletedRunUtc == null && restarted.StartUtc == now.AddDays(2).ToString("O"), "Manual replan uses chosen start and clears completion");
Console.WriteLine($"{checks} checks passed.");

namespace ScumRconTool
{
    public class BotSettings
    {
        public bool ChallengeRotationEnabled { get; set; }
        public int ChallengeRotationMaximum { get; set; }
        public int ChallengeRotationPauseHours { get; set; }
        public int ChallengeCompletedVisibleMinutes { get; set; }
        public List<WeeklyCommunityTaskDefinition> GetWeeklyTaskDefinitions() => new();
    }
}
namespace ScumRconTool.Services
{
    public class WeeklyCommunityTaskDefinition
    {
        public string Id { get; set; } = "";
        public bool Enabled { get; set; }
        public bool AutoRotate { get; set; }
        public string Type { get; set; } = "Daily";
        public string GoalScope { get; set; } = "Community";
        public string StartUtc { get; set; } = "";
        public string EndUtc { get; set; } = "old end";
        public DateTime? CompletedRunUtc { get; set; }
        public DateTime? LastScheduledUtc { get; set; }
        public DateTime? LastRunEndedUtc { get; set; }
    }
    public class WeeklyCommunityTaskProgress
    {
        public bool IsCompleted { get; set; }
        public WeeklyCommunityTaskDefinition Definition { get; set; } = new();
    }
    public static class WeeklyCommunityTaskService
    {
        public static int Resets;
        public static int DeleteSavedBaseline(WeeklyCommunityTaskDefinition _) => ++Resets;
        public static bool IsPersonalGoal(WeeklyCommunityTaskDefinition task) => task.GoalScope == "PerPlayer";
    }
    public static class WeeklyTaskDefinitionStore
    {
        public static void Save(IEnumerable<WeeklyCommunityTaskDefinition> _) { }
    }
}
