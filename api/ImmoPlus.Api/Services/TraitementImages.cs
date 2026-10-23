namespace ImmoPlus.Api.Services;

using SkiaSharp;

/// <summary>
/// Photos allégées pour les connexions 3G/4G (SkiaSharp, licence MIT) : réorientation selon l'EXIF, réduction sans recadrage
/// et réencodage JPEG — le JPEG produit ne contient aucune métadonnée (GPS, appareil, date de prise de vue…).
/// Les originaux déjà stockés ne sont jamais réécrits.
/// </summary>
public static class TraitementImages
{
    public const int TailleMaxPhoto = 1920;
    /// <summary>Panorama 360° équirectangulaire (2:1) : assez de détail pour la visite virtuelle.</summary>
    public const int TailleMaxPanorama = 6000;
    public const int TailleMaxEtatDesLieux = 1600;
    public const int TailleMiniature = 480;
    public const int QualiteJpeg = 82;
    public const int QualiteMiniature = 75;

    private static readonly SKSamplingOptions Echantillonnage = new(SKCubicResampler.Mitchell);

    /// <summary>
    /// Image décodée (à l'échelle la plus proche directement par le décodeur JPEG, pour limiter la mémoire des gros panoramas),
    /// remise à l'endroit et contenue dans un carré de tailleMax × tailleMax.
    /// </summary>
    /// <exception cref="InvalidOperationException">Fichier illisible (400 pour le client).</exception>
    public static async Task<SKBitmap> ChargerAsync(Stream flux, int tailleMax, CancellationToken annulation = default)
    {
        using var memoire = new MemoryStream();
        await flux.CopyToAsync(memoire, annulation);
        memoire.Position = 0;
        using var donnees = SKData.Create(memoire);
        using var codec = donnees is null ? null : SKCodec.Create(donnees);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
        {
            throw Illisible();
        }

        var taille = TailleDecodage(codec, tailleMax);
        var info = new SKImageInfo(taille.Width, taille.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var decodee = new SKBitmap(info);
        var resultat = codec.GetPixels(info, decodee.GetPixels());
        if (resultat is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            decodee.Dispose();
            throw Illisible();
        }

        var orientee = Orienter(decodee, codec.EncodedOrigin);
        return Reduire(orientee, tailleMax);
    }

    /// <summary>Plus petite échelle native du décodeur (1/8, 2/8… pour le JPEG) qui reste au moins à la taille voulue.</summary>
    private static SKSizeI TailleDecodage(SKCodec codec, int tailleMax)
    {
        var plusGrand = Math.Max(codec.Info.Width, codec.Info.Height);
        if (plusGrand <= tailleMax)
        {
            return codec.Info.Size;
        }
        for (var huitiemes = 1; huitiemes < 8; huitiemes++)
        {
            var dimensions = codec.GetScaledDimensions(huitiemes / 8f);
            if (Math.Max(dimensions.Width, dimensions.Height) >= tailleMax)
            {
                return dimensions;
            }
        }
        return codec.Info.Size;
    }

    /// <summary>Applique l'orientation EXIF (photo de téléphone prise en portrait) ; libère la source si une copie est faite.</summary>
    private static SKBitmap Orienter(SKBitmap source, SKEncodedOrigin origine)
    {
        if (origine == SKEncodedOrigin.TopLeft)
        {
            return source;
        }
        float l = source.Width, h = source.Height;
        // x' = ScaleX·x + SkewX·y + TransX ; y' = SkewY·x + ScaleY·y + TransY
        var (matrice, pivotee) = origine switch
        {
            SKEncodedOrigin.TopRight => (new SKMatrix(-1, 0, l, 0, 1, 0, 0, 0, 1), false),
            SKEncodedOrigin.BottomRight => (new SKMatrix(-1, 0, l, 0, -1, h, 0, 0, 1), false),
            SKEncodedOrigin.BottomLeft => (new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1), false),
            SKEncodedOrigin.LeftTop => (new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1), true),
            SKEncodedOrigin.RightTop => (new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1), true),
            SKEncodedOrigin.RightBottom => (new SKMatrix(0, -1, h, -1, 0, l, 0, 0, 1), true),
            SKEncodedOrigin.LeftBottom => (new SKMatrix(0, 1, 0, -1, 0, l, 0, 0, 1), true),
            _ => (SKMatrix.Identity, false)
        };
        var destination = new SKBitmap(new SKImageInfo(pivotee ? source.Height : source.Width, pivotee ? source.Width : source.Height,
            source.ColorType, source.AlphaType));
        using (var canevas = new SKCanvas(destination))
        {
            canevas.SetMatrix(matrice);
            canevas.DrawBitmap(source, 0, 0, Echantillonnage);
        }
        source.Dispose();
        return destination;
    }

    /// <summary>Réduit (sans recadrer) pour tenir dans tailleMax × tailleMax ; libère la source si une copie est faite.</summary>
    private static SKBitmap Reduire(SKBitmap source, int tailleMax)
    {
        var plusGrand = Math.Max(source.Width, source.Height);
        if (plusGrand <= tailleMax)
        {
            return source;
        }
        var ratio = (double)tailleMax / plusGrand;
        var info = new SKImageInfo(Math.Max(1, (int)Math.Round(source.Width * ratio)), Math.Max(1, (int)Math.Round(source.Height * ratio)),
            source.ColorType, source.AlphaType);
        var reduite = source.Resize(info, Echantillonnage) ?? throw Illisible();
        source.Dispose();
        return reduite;
    }

    /// <summary>JPEG sans métadonnées ; fond blanc pour les images transparentes (le JPEG n'a pas de transparence).</summary>
    private static byte[] EncoderJpeg(SKBitmap image, int qualite)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info) ?? throw Illisible();
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.DrawBitmap(image, 0, 0, Echantillonnage);
        using var instantane = surface.Snapshot();
        using var donnees = instantane.Encode(SKEncodedImageFormat.Jpeg, qualite) ?? throw Illisible();
        return donnees.ToArray();
    }

    /// <summary>Photo réencodée (JPEG sans métadonnées), prête à être enregistrée par IStockageFichiers.</summary>
    public static byte[] Jpeg(SKBitmap image, int qualite = QualiteJpeg) => EncoderJpeg(image, qualite);

    /// <summary>Miniature (480 px au plus, sans recadrage) d'une image déjà chargée, en JPEG.</summary>
    public static byte[] MiniatureJpeg(SKBitmap image)
    {
        var copie = image.Copy() ?? throw Illisible();
        using var miniature = Reduire(copie, TailleMiniature);
        return EncoderJpeg(miniature, QualiteMiniature);
    }

    /// <summary>Miniature JPEG en mémoire (ex. pour les PDF) ; null si le fichier est absent ou illisible.</summary>
    public static async Task<byte[]?> MiniatureJpegAsync(IStockageFichiers stockage, string cle, int tailleMax = TailleMiniature,
        CancellationToken annulation = default)
    {
        try
        {
            await using var flux = await stockage.OuvrirAsync(cle, annulation);
            if (flux is null)
            {
                return null;
            }
            using var image = await ChargerAsync(flux, tailleMax, annulation);
            return EncoderJpeg(image, QualiteMiniature);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or Amazon.Runtime.AmazonServiceException)
        {
            return null;
        }
    }

    private static InvalidOperationException Illisible() =>
        new("Image illisible ou endommagée. Formats acceptés : JPG, PNG, WEBP");
}
