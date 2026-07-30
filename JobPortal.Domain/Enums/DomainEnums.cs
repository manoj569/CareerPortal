namespace JobPortal.Domain.Enums;

public enum UserStatus { Pending = 1, Active, Suspended, Inactive }
public enum MembershipStatus { Pending = 1, Active, Suspended, Cancelled, Expired }
public enum PaymentStatus
{
    Pending = 1,
    Authorized,
    Paid,
    Failed,
    Refunded,
    Cancelled,
    Created,
    Expired
}
public enum PaymentProvider { Unknown = 0, Stripe, Razorpay, PayPal, BankTransfer }
public enum JobStatus { Draft = 1, Published, Paused, Closed, Expired, Archived }
public enum EmploymentType { FullTime = 1, PartTime, Contract, Internship, Freelance, Temporary }
public enum WorkplaceType { OnSite = 1, Remote, Hybrid }
public enum ExperienceLevel { Entry = 1, Junior, Mid, Senior, Lead, Executive }
public enum JobHistoryAction { Viewed = 1, Saved, Applied, Withdrawn, Rejected, Shortlisted, Hired }
public enum NotificationType { General = 1, Job, Application, Payment, Membership, Security }
public enum AuditAction
{
    Create = 1,
    Update,
    Delete,
    Restore,
    Login,
    Logout,
    Publish,
    Unpublish,
    Close,
    Archive,
    Feature,
    Unfeature,
    Upload,
    Submit,
    Withdraw,
    Confirm,
    WebhookSuccess,
    WebhookFailure,
    Activate
}
public enum SettingScope { Global = 1, User, Company }
public enum JobApplicationStatus { Submitted = 1, Reviewed, Shortlisted, Rejected, Withdrawn }
public enum CareerStage { Student = 1, Fresher, Experienced }
public enum DesiredOpportunity { Internship = 1, FresherJob, ExperiencedJob }
public enum WorkPreference { Remote = 1, Hybrid, OnSite }
