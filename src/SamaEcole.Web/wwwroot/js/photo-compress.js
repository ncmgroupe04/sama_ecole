/**
 * Compression client d'une photo avant envoi (feature B) — Canvas API. Redimensionne en carré 300×300
 * (recadrage centré sur le plus petit côté, pour ne jamais déformer un portrait ou un paysage) et
 * réencode en JPEG qualité 80 %, ramenant un fichier de plusieurs Mo à environ 30 Ko avant même de
 * quitter le navigateur — la photo voyage ensuite comme un simple champ base64 dans le JSON de
 * création/mise à jour, sans upload multipart ni endpoint binaire dédié.
 *
 * Utilisé par le composant Alpine partagé <photo-dropzone> (TagHelper, PhotoDropzoneTagHelper.cs).
 */
window.photoCompress = {
    /** @param {File} file @returns {Promise<string>} base64 JPEG SANS le préfixe "data:image/jpeg;base64,". */
    async compress(file) {
        if (!file.type || !file.type.startsWith('image/')) {
            throw new Error('Le fichier doit être une image.');
        }

        const dataUrl = await this._readAsDataUrl(file);
        const img = await this._loadImage(dataUrl);

        const size = 300;
        const canvas = document.createElement('canvas');
        canvas.width = size;
        canvas.height = size;
        const ctx = canvas.getContext('2d');

        const side = Math.min(img.naturalWidth, img.naturalHeight);
        const sx = (img.naturalWidth - side) / 2;
        const sy = (img.naturalHeight - side) / 2;
        ctx.drawImage(img, sx, sy, side, side, 0, 0, size, size);

        const jpegDataUrl = canvas.toDataURL('image/jpeg', 0.8);
        return jpegDataUrl.slice(jpegDataUrl.indexOf(',') + 1);
    },

    _readAsDataUrl(file) {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.onload = () => resolve(reader.result);
            reader.onerror = () => reject(new Error('Lecture du fichier impossible.'));
            reader.readAsDataURL(file);
        });
    },

    _loadImage(src) {
        return new Promise((resolve, reject) => {
            const img = new Image();
            img.onload = () => resolve(img);
            img.onerror = () => reject(new Error('Image invalide ou corrompue.'));
            img.src = src;
        });
    }
};
