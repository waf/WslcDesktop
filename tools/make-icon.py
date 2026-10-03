"""Draws the app icon (a cube on a rounded blue tile) into src/WslcDesktop.App/Assets/app.ico. Needs Pillow."""
from PIL import Image, ImageDraw

def draw(size):
    scale = 4  # supersample for smooth edges
    s = size * scale
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((0, 0, s - 1, s - 1), radius=int(s * 0.22), fill=(37, 99, 235, 255))
    # Isometric cube.
    cx, top, w, h = s / 2, s * 0.20, s * 0.30, s * 0.17
    t = (cx, top); l = (cx - w, top + h); r = (cx + w, top + h); c = (cx, top + 2 * h)
    bl = (l[0], l[1] + s * 0.30); br = (r[0], r[1] + s * 0.30); bc = (c[0], c[1] + s * 0.30)
    d.polygon([t, r, c, l], fill=(255, 255, 255, 255))
    d.polygon([l, c, bc, bl], fill=(191, 219, 254, 255))
    d.polygon([c, r, br, bc], fill=(147, 197, 253, 255))
    return img.resize((size, size), Image.LANCZOS)

sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
images = [draw(n) for n in sizes]
images[-1].save("src/WslcDesktop.App/Assets/app.ico", sizes=[(n, n) for n in sizes], append_images=images[:-1])
print("wrote app.ico")
