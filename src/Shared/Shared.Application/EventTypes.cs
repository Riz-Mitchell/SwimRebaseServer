namespace Shared.Application;

/// <summary>Every event type currently tracked via <see cref="IEventTracker"/>, across all features.</summary>
public static class EventTypes
{
    public const string UserSignedUp = "user_signed_up";
    public const string AiMessageSent = "ai_message_sent";
    public const string FocusCreated = "focus_created";
    public const string FocusEdited = "focus_edited";
    public const string FocusDeleted = "focus_deleted";
    public const string TimetableSlotUpserted = "timetable_slot_upserted";
    public const string TimetableSlotDeleted = "timetable_slot_deleted";
    public const string OnboardingStepCompleted = "onboarding_step_completed";
    public const string OnboardingCompleted = "onboarding_completed";
}
