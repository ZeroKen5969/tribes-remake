import java.awt.image.BufferedImage;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import javax.imageio.ImageIO;

/**
 * Builds the app icons of the Godot client from the original art:
 *   game/res/icon.png (48x48, the MIDlet icon) and
 *   android/app/src/main/res/mipmap-xxxhdpi/ic_launcher.png (192x192, the earlier Android port).
 * Writes into client/src/Platform/Icons/:
 *   icon-192.png                 Android legacy launcher icon
 *   adaptive-foreground-432.png  Android adaptive icon: the art at 6x inside the 72dp visible area
 *   adaptive-background-432.png  Android adaptive icon: the art's most common border colour
 *   icon.ico                     Windows executable icon (16 to 256 pixels)
 * Enlargements use whole-number nearest-neighbour scaling so the pixel art stays sharp; the two
 * smallest Windows sizes are averaged down instead.
 *
 * usage: java tools/build/MakeIcons.java [REPO_ROOT]
 */
public final class MakeIcons {
    private static final int ADAPTIVE_SIZE = 432;
    private static final int ADAPTIVE_ART_SCALE = 6;

    private MakeIcons() {
    }

    public static void main(String[] args) throws IOException {
        Path root = Path.of(args.length > 0 ? args[0] : ".").toAbsolutePath().normalize();
        BufferedImage art = read(root.resolve("game/res/icon.png"));
        BufferedImage launcher = read(root.resolve("android/app/src/main/res/mipmap-xxxhdpi/ic_launcher.png"));
        Path out = root.resolve("client/src/Platform/Icons");
        Files.createDirectories(out);

        write(launcher, out.resolve("icon-192.png"));

        BufferedImage foreground = new BufferedImage(ADAPTIVE_SIZE, ADAPTIVE_SIZE, BufferedImage.TYPE_INT_ARGB);
        int artSize = art.getWidth() * ADAPTIVE_ART_SCALE;
        int offset = (ADAPTIVE_SIZE - artSize) / 2;
        draw(scaleNearest(art, ADAPTIVE_ART_SCALE), foreground, offset, offset);
        write(foreground, out.resolve("adaptive-foreground-432.png"));

        BufferedImage background = new BufferedImage(ADAPTIVE_SIZE, ADAPTIVE_SIZE, BufferedImage.TYPE_INT_ARGB);
        int colour = mostCommonBorderColour(art) | 0xFF000000;
        for (int y = 0; y < ADAPTIVE_SIZE; y++) {
            for (int x = 0; x < ADAPTIVE_SIZE; x++) {
                background.setRGB(x, y, colour);
            }
        }
        write(background, out.resolve("adaptive-background-432.png"));

        List<BufferedImage> icoImages = new ArrayList<>();
        icoImages.add(scaleArea(art, 16));
        icoImages.add(scaleArea(art, 32));
        icoImages.add(toArgb(art));
        icoImages.add(padded(scaleNearest(art, 1), 64));
        icoImages.add(padded(scaleNearest(art, 2), 128));
        icoImages.add(padded(scaleNearest(art, 5), 256));
        writeIco(icoImages, out.resolve("icon.ico"));
        System.out.println("icons written to " + out);
    }

    private static BufferedImage read(Path path) throws IOException {
        BufferedImage image = ImageIO.read(path.toFile());
        if (image == null) {
            throw new IOException("not an image: " + path);
        }
        return toArgb(image);
    }

    private static void write(BufferedImage image, Path path) throws IOException {
        if (!ImageIO.write(image, "png", path.toFile())) {
            throw new IOException("cannot write " + path);
        }
    }

    private static BufferedImage toArgb(BufferedImage image) {
        BufferedImage copy = new BufferedImage(image.getWidth(), image.getHeight(), BufferedImage.TYPE_INT_ARGB);
        draw(image, copy, 0, 0);
        return copy;
    }

    private static void draw(BufferedImage source, BufferedImage target, int left, int top) {
        for (int y = 0; y < source.getHeight(); y++) {
            for (int x = 0; x < source.getWidth(); x++) {
                target.setRGB(left + x, top + y, source.getRGB(x, y));
            }
        }
    }

    private static BufferedImage scaleNearest(BufferedImage source, int factor) {
        BufferedImage scaled = new BufferedImage(source.getWidth() * factor, source.getHeight() * factor, BufferedImage.TYPE_INT_ARGB);
        for (int y = 0; y < scaled.getHeight(); y++) {
            for (int x = 0; x < scaled.getWidth(); x++) {
                scaled.setRGB(x, y, source.getRGB(x / factor, y / factor));
            }
        }
        return scaled;
    }

    /** Centres the image on a transparent square of the given size. */
    private static BufferedImage padded(BufferedImage source, int size) {
        BufferedImage canvas = new BufferedImage(size, size, BufferedImage.TYPE_INT_ARGB);
        draw(source, canvas, (size - source.getWidth()) / 2, (size - source.getHeight()) / 2);
        return canvas;
    }

    /** Shrinks the image to size x size by averaging the source area under each pixel (alpha weighted). */
    private static BufferedImage scaleArea(BufferedImage source, int size) {
        BufferedImage scaled = new BufferedImage(size, size, BufferedImage.TYPE_INT_ARGB);
        double step = (double) source.getWidth() / size;
        for (int ty = 0; ty < size; ty++) {
            for (int tx = 0; tx < size; tx++) {
                scaled.setRGB(tx, ty, averageArea(source, tx * step, ty * step, step));
            }
        }
        return scaled;
    }

    private static int averageArea(BufferedImage source, double left, double top, double step) {
        double a = 0;
        double r = 0;
        double g = 0;
        double b = 0;
        double total = 0;
        for (int sy = (int) Math.floor(top); sy < Math.ceil(top + step); sy++) {
            double wy = Math.min(sy + 1, top + step) - Math.max(sy, top);
            for (int sx = (int) Math.floor(left); sx < Math.ceil(left + step); sx++) {
                double wx = Math.min(sx + 1, left + step) - Math.max(sx, left);
                double weight = wx * wy;
                int argb = source.getRGB(sx, sy);
                double alpha = (argb >>> 24) / 255.0;
                a += alpha * weight;
                r += ((argb >> 16) & 0xFF) * alpha * weight;
                g += ((argb >> 8) & 0xFF) * alpha * weight;
                b += (argb & 0xFF) * alpha * weight;
                total += weight;
            }
        }
        if (a <= 0) {
            return 0;
        }
        int outA = (int) Math.round(a / total * 255);
        int outR = (int) Math.round(r / a);
        int outG = (int) Math.round(g / a);
        int outB = (int) Math.round(b / a);
        return (outA << 24) | (outR << 16) | (outG << 8) | outB;
    }

    private static int mostCommonBorderColour(BufferedImage image) {
        Map<Integer, Integer> counts = new HashMap<>();
        int w = image.getWidth();
        int h = image.getHeight();
        for (int x = 0; x < w; x++) {
            counts.merge(image.getRGB(x, 0) & 0xFFFFFF, 1, Integer::sum);
            counts.merge(image.getRGB(x, h - 1) & 0xFFFFFF, 1, Integer::sum);
        }
        for (int y = 1; y < h - 1; y++) {
            counts.merge(image.getRGB(0, y) & 0xFFFFFF, 1, Integer::sum);
            counts.merge(image.getRGB(w - 1, y) & 0xFFFFFF, 1, Integer::sum);
        }
        return counts.entrySet().stream().max(Map.Entry.comparingByValue()).orElseThrow().getKey();
    }

    /** Writes a .ico whose entries are PNG images (supported by Windows Vista and later). */
    private static void writeIco(List<BufferedImage> images, Path path) throws IOException {
        List<byte[]> pngs = new ArrayList<>();
        for (BufferedImage image : images) {
            ByteArrayOutputStream bytes = new ByteArrayOutputStream();
            ImageIO.write(image, "png", bytes);
            pngs.add(bytes.toByteArray());
        }
        int headerSize = 6 + 16 * images.size();
        int total = headerSize + pngs.stream().mapToInt(p -> p.length).sum();
        ByteBuffer ico = ByteBuffer.allocate(total).order(ByteOrder.LITTLE_ENDIAN);
        ico.putShort((short) 0).putShort((short) 1).putShort((short) images.size());
        int dataOffset = headerSize;
        for (int i = 0; i < images.size(); i++) {
            BufferedImage image = images.get(i);
            ico.put((byte) (image.getWidth() >= 256 ? 0 : image.getWidth()));
            ico.put((byte) (image.getHeight() >= 256 ? 0 : image.getHeight()));
            ico.put((byte) 0).put((byte) 0);
            ico.putShort((short) 1).putShort((short) 32);
            ico.putInt(pngs.get(i).length).putInt(dataOffset);
            dataOffset += pngs.get(i).length;
        }
        for (byte[] png : pngs) {
            ico.put(png);
        }
        Files.write(path, ico.array());
    }
}
