using System.ComponentModel.DataAnnotations;
using Vocab_LearningApp.Models.Domain;

namespace Vocab_LearningApp.Models.Requests;

public sealed class LoginInputModel
{
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
    [MinLength(6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự.")]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}

public sealed class RegisterInputModel
{
    [Required(ErrorMessage = "Họ và tên là bắt buộc.")]
    [MaxLength(255)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
    [MinLength(6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự.")]
    [MaxLength(100)]
    public string Password { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? LearningGoal { get; set; }

    [MaxLength(10)]
    public string? CurrentLevel { get; set; }

    [Range(1, 200, ErrorMessage = "Mục tiêu mỗi ngày phải từ 1 đến 200 từ.")]
    public int? DailyTarget { get; set; }

    public bool AcceptTerms { get; set; }
}

public sealed class GoogleLoginRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string GoogleSub { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? AvatarUrl { get; set; }

    [MaxLength(50)]
    public string? LearningGoal { get; set; }

    [MaxLength(10)]
    public string? CurrentLevel { get; set; }

    [Range(1, 200)]
    public int? DailyTarget { get; set; }
}

public class CreateDeckRequest
{
    [Required]
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? Description { get; set; }

    [MaxLength(100)]
    public string? Tags { get; set; }

    public bool IsPublic { get; set; }
}

public sealed class UpdateDeckRequest : CreateDeckRequest
{
}

public class CreateVocabularyRequest
{
    [Required]
    [MaxLength(50)]
    public string Word { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Pronunciation { get; set; }

    public int? PartOfSpeech { get; set; }

    [MaxLength(255)]
    public string? MeaningVi { get; set; }

    [MaxLength(255)]
    public string? DescriptionEn { get; set; }

    [MaxLength(255)]
    public string? ExampleSentence { get; set; }

    [MaxLength(100)]
    public string? Collocations { get; set; }

    [MaxLength(255)]
    public string? RelatedWords { get; set; }

    [MaxLength(255)]
    public string? Note { get; set; }

    [MaxLength(255)]
    public string? ImageUrl { get; set; }

    [MaxLength(255)]
    public string? AudioUrl { get; set; }
}

public sealed class UpdateVocabularyRequest : CreateVocabularyRequest
{
}

public sealed class ReviewRequest
{
    [Required]
    public long VocabularyId { get; set; }

    [Required]
    public ReviewRating Rating { get; set; }

    public long? DeckId { get; set; }
}

public sealed class DeckImportRequest
{
    [Required]
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? Description { get; set; }

    [MaxLength(100)]
    public string? Tags { get; set; }

    public bool IsPublic { get; set; }
}

public sealed class EditProfileRequest
{
    [Required(ErrorMessage = "Họ và tên là bắt buộc.")]
    [MaxLength(255)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? AvatarUrl { get; set; }

    [MaxLength(50)]
    public string? LearningGoal { get; set; }

    [MaxLength(10)]
    public string? CurrentLevel { get; set; }

    [Range(1, 200, ErrorMessage = "Mục tiêu mỗi ngày phải từ 1 đến 200 từ.")]
    public int? DailyTarget { get; set; }
}

public sealed class ChangePasswordRequest
{
    [Required(ErrorMessage = "Mật khẩu hiện tại là bắt buộc.")]
    [MinLength(6, ErrorMessage = "Mật khẩu hiện tại phải có ít nhất 6 ký tự.")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu mới là bắt buộc.")]
    [MinLength(6, ErrorMessage = "Mật khẩu mới phải có ít nhất 6 ký tự.")]
    [MaxLength(100)]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Xác nhận mật khẩu là bắt buộc.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Mật khẩu xác nhận không khớp.")]
    public string ConfirmNewPassword { get; set; } = string.Empty;
}
