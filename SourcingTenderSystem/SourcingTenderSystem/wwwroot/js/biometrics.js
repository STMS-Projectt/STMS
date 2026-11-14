/**
 * Biometric Authentication — Client-Side Engine
 * Face: captures a live webcam JPEG and sends it to the server for Face++ processing.
 * Fingerprint: uses WebAuthn platform authenticator (Windows Hello / Touch ID).
 */
class BiometricManager {
    constructor() {
        this.stream = null;
    }

    // ─────────────── CAMERA ───────────────

    /** Starts the live webcam stream into a <video> element. */
    async startCamera(videoElementId) {
        const video = document.getElementById(videoElementId);
        if (!video) throw new Error("Video element not found.");

        if (this.stream) this.stopCamera();

        try {
            this.stream = await navigator.mediaDevices.getUserMedia({
                video: { width: { ideal: 640 }, height: { ideal: 480 }, facingMode: "user" },
                audio: false
            });
            video.srcObject = this.stream;
            await video.play();
        } catch {
            throw new Error("Camera access is required. Please allow camera permissions in your browser.");
        }
    }

    /** Stops the active webcam stream. */
    stopCamera() {
        if (this.stream) {
            this.stream.getTracks().forEach(t => t.stop());
            this.stream = null;
        }
    }

    /**
     * Captures a JPEG snapshot from the live video stream and returns it as a base64 string.
     * Throws if the camera is not active (prevents static image spoofing).
     */
    captureSnapshot(videoElementId, quality = 0.85) {
        const video = document.getElementById(videoElementId);
        if (!video || !video.srcObject || video.readyState < 2) {
            throw new Error("Live camera stream is required. Please open the camera first.");
        }

        const canvas = document.createElement("canvas");
        const vw = video.videoWidth || 640;
        const vh = video.videoHeight || 480;
        const scale = Math.min(1, 640 / Math.max(vw, vh));
        canvas.width = Math.round(vw * scale);
        canvas.height = Math.round(vh * scale);

        const ctx = canvas.getContext("2d");
        ctx.drawImage(video, 0, 0, canvas.width, canvas.height);

        // Returns full data-URI: "data:image/jpeg;base64,..."
        return canvas.toDataURL("image/jpeg", quality);
    }

    // ─────────────── FINGERPRINT (WebAuthn) ───────────────

    /** Registers a new WebAuthn platform credential (fingerprint / Windows Hello). */
    async enrollFingerprint(userEmail) {
        this._requireWebAuthn();

        const challenge = crypto.getRandomValues(new Uint8Array(32));
        const userId = new TextEncoder().encode(userEmail);

        try {
            const credential = await navigator.credentials.create({
                publicKey: {
                    challenge,
                    rp: { name: "Sourcing & Tender Management System", id: window.location.hostname },
                    user: { id: userId, name: userEmail, displayName: userEmail },
                    pubKeyCredParams: [
                        { type: "public-key", alg: -7 },   // ES256
                        { type: "public-key", alg: -257 }  // RS256
                    ],
                    authenticatorSelection: { authenticatorAttachment: "platform", userVerification: "required" },
                    timeout: 60000,
                    attestation: "none"
                }
            });
            return { credentialId: this._bufferToBase64(credential.rawId) };
        } catch (err) {
            if (err.name === "NotAllowedError")
                throw new Error("Fingerprint sensor not available or request was cancelled.");
            throw err;
        }
    }

    /** Authenticates using a previously enrolled WebAuthn credential. */
    async verifyFingerprint() {
        this._requireWebAuthn();

        const challenge = crypto.getRandomValues(new Uint8Array(32));

        try {
            const assertion = await navigator.credentials.get({
                publicKey: {
                    challenge,
                    rpId: window.location.hostname,
                    userVerification: "required",
                    timeout: 60000
                }
            });
            return { credentialId: this._bufferToBase64(assertion.rawId) };
        } catch (err) {
            if (err.name === "NotAllowedError")
                throw new Error("Fingerprint sensor not available or request was cancelled.");
            throw err;
        }
    }

    // ─────────────── HELPERS ───────────────

    _requireWebAuthn() {
        if (!window.PublicKeyCredential)
            throw new Error("WebAuthn is not supported by this browser.");
    }

    _bufferToBase64(buffer) {
        const bytes = new Uint8Array(buffer);
        let binary = "";
        for (let i = 0; i < bytes.byteLength; i++) binary += String.fromCharCode(bytes[i]);
        return window.btoa(binary);
    }
}

window.biometrics = new BiometricManager();
