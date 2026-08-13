using NUnit.Framework;
using SaigonWaterbus.Application.Assistant;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Assistant;

public class AssistantLanguageTests
{
    [TestCase("VN")]
    [TestCase("vn")]
    [TestCase("vi")]
    [TestCase("vi-VN")]
    [TestCase("vi_VN")]
    [TestCase("Vietnamese")]
    [TestCase("  vi  ")]
    public void NhanDienTiengViet(string input) =>
        AssistantLanguage.Resolve(input).ShouldBe(AssistantLanguage.Vietnamese);

    [TestCase("ENG")]
    [TestCase("eng")]
    [TestCase("en")]
    [TestCase("en-US")]
    [TestCase("en_GB")]
    [TestCase("English")]
    public void NhanDienTiengAnh(string input) =>
        AssistantLanguage.Resolve(input).ShouldBe(AssistantLanguage.English);

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("fr")]
    [TestCase("ja-JP")]
    [TestCase("rac")]
    public void KhongNhanRaThiTraNullDeTuPhatHien(string? input) =>
        AssistantLanguage.Resolve(input).ShouldBeNull();

    [TestCase("start")]
    [TestCase("Start")]
    [TestCase("  START  ")]
    [TestCase("start.")]
    public void TinNhanMoiStart_ChaoBangTiengAnh(string input) =>
        AssistantLanguage.ResolveGreetingTrigger(input).ShouldBe(AssistantLanguage.English);

    [TestCase("bắt đầu")]
    [TestCase("Bắt đầu")]
    [TestCase("BẮT ĐẦU")]
    [TestCase("bat dau")]
    [TestCase("bắt đầu!")]
    public void TinNhanMoiBatDau_ChaoBangTiengViet(string input) =>
        AssistantLanguage.ResolveGreetingTrigger(input).ShouldBe(AssistantLanguage.Vietnamese);

    // Cau hoi that thi de tro ly tu bam ngon ngu, dung ep theo tu khoa nam giua cau.
    [TestCase(null)]
    [TestCase("")]
    [TestCase("bắt đầu đặt vé giúp mình")]
    [TestCase("start booking for me")]
    [TestCase("mai co chuyen nao khong")]
    public void KhongPhaiTinNhanMoi_ThiTraNull(string? input) =>
        AssistantLanguage.ResolveGreetingTrigger(input).ShouldBeNull();

    [Test]
    public void ChiDanPromptKhacNhauTheoTungNgonNgu()
    {
        var vi = AssistantLanguage.PromptInstruction(AssistantLanguage.Vietnamese);
        var en = AssistantLanguage.PromptInstruction(AssistantLanguage.English);
        var auto = AssistantLanguage.PromptInstruction(null);

        vi.ShouldContain("TIẾNG VIỆT");
        en.ShouldContain("TIẾNG ANH");
        // Ep tieng Anh thi cau tu choi va cau "chua co thong tin" cung phai tieng Anh.
        en.ShouldContain("từ chối");
        // Khong biet ngon ngu thi bam theo tin nhan khach, khong ep buoc.
        auto.ShouldContain("ĐÚNG NGÔN NGỮ khách đang dùng");

        // Ten ga la ten rieng -> ca ba che do deu phai giu nguyen tieng Viet.
        en.ShouldContain("giữ nguyên tiếng Việt");
        auto.ShouldContain("giữ nguyên tiếng Việt");
    }
}
