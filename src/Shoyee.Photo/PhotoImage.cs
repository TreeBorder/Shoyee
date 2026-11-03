namespace Shoyee.Photo;

/// <summary>一张生成图片（字节 + 媒体类型；落盘由 <see cref="PhotoService"/> 负责）。</summary>
/// <param name="Mime">媒体类型，如 <c>image/png</c>。</param>
/// <param name="Data">图片字节。</param>
/// <param name="Extension">扩展名（含点，如 <c>.png</c>）。</param>
public sealed record PhotoImage(string Mime, byte[] Data, string Extension = ".png");
