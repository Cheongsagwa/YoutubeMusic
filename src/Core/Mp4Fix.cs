using System.Buffers.Binary;

namespace OneMusic.Core;

/// <summary>
/// YouTube 오디오(조각난 MP4) 중 일부 곡에는 '편집 목록(edts)' 상자가 들어 있는데,
/// Windows 기본 재생기가 이걸 만나면 재생하자마자 끝나 버린다.
/// 상자 이름만 'free'(빈 공간) 로 바꿔서 재생기가 무시하게 한다 — 크기가 그대로라 다른 위치 정보는 안 바뀐다.
/// (빠지는 건 곡 맨 앞 0.04초 정도의 인코더 여백 처리뿐)
/// </summary>
public static class Mp4Fix
{
    public static int NeutralizeEditLists(byte[] b)
    {
        var count = 0;
        Walk(b, 0, b.Length, 0, ref count);
        return count;
    }

    private static void Walk(byte[] b, long start, long end, int depth, ref int count)
    {
        var off = start;
        while (off + 8 <= end)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan((int)off, 4));
            var hdr = 8;
            if (size == 1)
            {
                if (off + 16 > end) return;
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(b.AsSpan((int)off + 8, 8));
                hdr = 16;
            }
            else if (size == 0) size = end - off;
            if (size < 8 || off + size > end) return;
            var type = System.Text.Encoding.ASCII.GetString(b, (int)off + 4, 4);
            if (type == "edts")
            {
                b[off + 4] = (byte)'f'; b[off + 5] = (byte)'r'; b[off + 6] = (byte)'e'; b[off + 7] = (byte)'e';
                count++;
            }
            else if (depth < 4 && type is "moov" or "trak")
                Walk(b, off + hdr, off + size, depth + 1, ref count);
            // moov 다음(조각 데이터)은 볼 필요 없다
            if (depth == 0 && type == "moov") return;
            off += size;
        }
    }
}
