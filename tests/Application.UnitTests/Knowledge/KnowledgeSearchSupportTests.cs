using NUnit.Framework;
using SaigonWaterbus.Application.Knowledge;
using SaigonWaterbus.Domain.Constants;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Knowledge;

public class KnowledgeSearchSupportTests
{
    private static KnowledgeSearchCandidate Entry(
        string title,
        string content,
        string[]? keywords = null,
        string category = KnowledgeCategories.Other,
        int displayOrder = 0) =>
        new(title, content, category, keywords ?? [], displayOrder);

    private static readonly KnowledgeSearchCandidate Refund = Entry(
        "Chính sách hoàn vé",
        "Vé được hoàn trước giờ khởi hành 24 tiếng, phí hoàn 10%.",
        ["hoàn vé", "trả lại vé", "refund"],
        KnowledgeCategories.Refund);

    private static readonly KnowledgeSearchCandidate Luggage = Entry(
        "Quy định hành lý",
        "Mỗi khách mang tối đa 20kg hành lý, không mang chất dễ cháy.",
        ["hành lý", "mang đồ", "kg"],
        KnowledgeCategories.Luggage);

    private static readonly KnowledgeSearchCandidate[] Corpus = [Refund, Luggage];

    [Test]
    public void GoDauHoacKhongDauDeuKhopCungMotEntry()
    {
        KnowledgeSearchSupport.Rank(Corpus, "chính sách hoàn vé thế nào")
            .ShouldHaveSingleItem().Title.ShouldBe(Refund.Title);

        // Khach thuong go khong dau — phai ra ket qua y het.
        KnowledgeSearchSupport.Rank(Corpus, "chinh sach hoan ve the nao")
            .ShouldHaveSingleItem().Title.ShouldBe(Refund.Title);
    }

    [Test]
    public void KhopTuKhoaXepTrenKhopNoiDung()
    {
        // "tra lai ve" chi co trong Keywords cua Refund, khong co trong title/content.
        var hits = KnowledgeSearchSupport.Rank(Corpus, "trả lại vé");

        hits.ShouldNotBeEmpty();
        hits[0].Title.ShouldBe(Refund.Title);
    }

    [Test]
    public void EntryCoDiemCaoHonXepTruoc()
    {
        var mentionsLuggageOnce = Entry("Dịch vụ trên tàu", "Có chỗ để hành lý nhỏ.");
        var corpus = new[] { mentionsLuggageOnce, Luggage };

        var hits = KnowledgeSearchSupport.Rank(corpus, "quy định hành lý bao nhiêu kg");

        hits[0].Title.ShouldBe(Luggage.Title);
    }

    [Test]
    public void CauKhongLienQuanTraVeRong()
    {
        KnowledgeSearchSupport.Rank(Corpus, "thủ đô nước Pháp là gì").ShouldBeEmpty();
        KnowledgeSearchSupport.Rank(Corpus, "tỉ số bóng đá tối qua").ShouldBeEmpty();
    }

    [Test]
    public void MotTokenDaiKhopTronTuKhoaThiDuDeNhan()
    {
        // "What is your refund policy?" chi khop DUNG MOT tu khoa ("refund") vi noi dung entry
        // la tieng Viet. Duoi nguong 2 token nhung phai nhan, vi Keywords do admin khai nen
        // khop tron mot tu khoa dai la bang chung du manh.
        var tokens = KnowledgeSearchSupport.Tokenize("What is your refund policy?");
        var match = KnowledgeSearchSupport.Score(Refund, tokens);

        match.MatchedTokens.ShouldBe(1);
        match.HasStrongKeywordHit.ShouldBeTrue();
        KnowledgeSearchSupport.Rank(Corpus, "What is your refund policy?")
            .ShouldHaveSingleItem().Title.ShouldBe(Refund.Title);
    }

    [Test]
    public void KhopKhongTronTuHoacTokenNganThiKhongPhaiBangChungManh()
    {
        // Token ngan: "do" (tu "thu do") co trong tu khoa "mang do" nhung chi 2 ky tu -> bi
        // nguong do dai loai.
        var shortToken = KnowledgeSearchSupport.Tokenize("thủ đô nước Pháp là gì");
        KnowledgeSearchSupport.Score(Luggage, shortToken).HasStrongKeywordHit.ShouldBeFalse();

        // Token dai nhung chi khop LAP LUNG giua tu khoa ("thanhtoan" nam trong
        // "thanhtoanonline") -> khong phai khop tron tu nen cung khong manh.
        var entry = Entry("Thanh toán", "Nội dung.", ["thanhtoanonline"]);
        var partial = KnowledgeSearchSupport.Tokenize("cách thanhtoan thế nào");
        var match = KnowledgeSearchSupport.Score(entry, partial);

        partial.Length.ShouldBe(2);          // cach, thanhtoan (the/nao la hu tu)
        match.MatchedTokens.ShouldBe(1);     // chi "thanhtoan" khop
        match.HasStrongKeywordHit.ShouldBeFalse();
        KnowledgeSearchSupport.Rank([entry], "cách thanhtoan thế nào").ShouldBeEmpty();
    }

    [Test]
    public void MotTokenYeuKhopTinhCoKhongDuDeTraKetQua()
    {
        // "thủ đô" bỏ dấu thành "thu do", token "do" khớp vào từ khoá "mang đồ" của mục hành lý.
        // Chỉ 1/4 token khớp -> phải bị loại, nếu không bot sẽ đem quy định hành lý ra trả lời
        // câu hỏi về thủ đô nước Pháp.
        var tokens = KnowledgeSearchSupport.Tokenize("thủ đô nước Pháp là gì");
        var match = KnowledgeSearchSupport.Score(Luggage, tokens);

        match.Score.ShouldBeGreaterThan(0);
        match.MatchedTokens.ShouldBe(1);
        KnowledgeSearchSupport.Rank([Luggage], "thủ đô nước Pháp là gì").ShouldBeEmpty();
    }

    [Test]
    public void CauChiGomHuTuTraVeRongChuKhongTraCaCorpus()
    {
        // Neu khong chan, cau nay se khop bua vao moi entry va model tuong la cau tra loi.
        KnowledgeSearchSupport.Tokenize("là gì thế nào có không").ShouldBeEmpty();
        KnowledgeSearchSupport.Rank(Corpus, "là gì thế nào có không").ShouldBeEmpty();
    }

    [Test]
    public void QueryRongHoacNullTraVeRong()
    {
        KnowledgeSearchSupport.Rank(Corpus, null).ShouldBeEmpty();
        KnowledgeSearchSupport.Rank(Corpus, "   ").ShouldBeEmpty();
    }

    [Test]
    public void TokenQuaNganBiBo()
    {
        // "o" (1 ky tu) se khop vao gan het moi text -> phai bi loai.
        KnowledgeSearchSupport.Tokenize("ở o a").ShouldBeEmpty();
    }

    [Test]
    public void TakeBiGioiHanTheoMaxTake()
    {
        var many = Enumerable.Range(0, 10)
            .Select(i => Entry($"Hành lý mục {i}", "Nội dung hành lý.", ["hành lý"], displayOrder: i))
            .ToArray();

        KnowledgeSearchSupport.Rank(many, "hành lý", take: 99)
            .Count.ShouldBe(KnowledgeSearchSupport.MaxTake);

        KnowledgeSearchSupport.Rank(many, "hành lý", take: 0)
            .Count.ShouldBe(1);
    }

    [Test]
    public void CungDiemThiSapTheoDisplayOrder()
    {
        var second = Entry("Hành lý B", "Nội dung hành lý.", ["hành lý"], displayOrder: 5);
        var first = Entry("Hành lý A", "Nội dung hành lý.", ["hành lý"], displayOrder: 1);

        var hits = KnowledgeSearchSupport.Rank([second, first], "hành lý", take: 2);

        hits[0].Title.ShouldBe("Hành lý A");
    }

    [Test]
    public void ContentDaiBiCatDeKhongPhinhContextCuaLlm()
    {
        var longContent = new string('x', KnowledgeSearchSupport.MaxContentChars + 500);

        var truncated = KnowledgeSearchSupport.TruncateContent(longContent);

        truncated.Length.ShouldBe(KnowledgeSearchSupport.MaxContentChars + 3);
        truncated.ShouldEndWith("...");
        KnowledgeSearchSupport.TruncateContent("ngan gon").ShouldBe("ngan gon");
    }
}
