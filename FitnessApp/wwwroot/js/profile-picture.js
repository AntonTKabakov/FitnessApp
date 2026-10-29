(() => {
    "use strict";
    const editor = document.querySelector("[data-profile-picture]");
    if (!editor) return;

    const image = editor.querySelector("[data-picture-image]");
    const fileInput = editor.querySelector("[data-picture-file]");
    const chooseButton = editor.querySelector("[data-picture-choose]");
    const uploadButton = editor.querySelector("[data-picture-upload]");
    const cancelButton = editor.querySelector("[data-picture-cancel]");
    const removeButton = editor.querySelector("[data-picture-remove]");
    const selection = editor.querySelector("[data-picture-selection]");
    const message = editor.querySelector("[data-picture-message]");
    const progressWrap = editor.querySelector("[data-picture-progress-wrap]");
    const progress = editor.querySelector("[data-picture-progress]");
    const progressLabel = editor.querySelector("[data-picture-progress-label]");
    const token = editor.querySelector('input[name="__RequestVerificationToken"]').value;
    let savedUrl = image.src;
    let hasPicture = editor.dataset.hasPicture === "true";
    let selectedFile = null;
    let previewUrl = null;
    let busy = false;

    const showMessage = (text, isError = false) => {
        message.textContent = text;
        message.classList.toggle("notice-error", isError);
        message.classList.toggle("notice-success", !isError);
        message.hidden = !text;
    };

    const releasePreview = () => {
        if (previewUrl) URL.revokeObjectURL(previewUrl);
        previewUrl = null;
    };

    const updateControls = () => {
        [chooseButton, uploadButton, cancelButton, removeButton, fileInput].forEach(control => { control.disabled = busy; });
        uploadButton.hidden = !selectedFile;
        cancelButton.hidden = !selectedFile;
        removeButton.hidden = !hasPicture || !!selectedFile;
        selection.hidden = !selectedFile;
        editor.setAttribute("aria-busy", String(busy));
        editor.dataset.hasPicture = String(hasPicture);
    };

    const resetSelection = () => {
        selectedFile = null;
        fileInput.value = "";
        image.src = savedUrl;
        image.alt = "Your profile picture";
        releasePreview();
        updateControls();
    };

    const errorMessage = (xhr) => {
        if (xhr.status === 401) return "Your session has expired. Sign in again, then retry.";
        if (xhr.status === 403) return "You do not have permission to change this profile picture.";
        if (xhr.status === 413) return "Choose an image no larger than 5 MB.";
        if (xhr.status === 415) return "Choose a JPEG, PNG, or WebP image with the correct file extension.";
        try {
            const body = JSON.parse(xhr.responseText);
            if (typeof body.error === "string") return body.error;
        } catch { /* Non-JSON failures receive a safe, useful message. */ }
        if (xhr.status === 400) return "The picture could not be accepted. Check the image, or reload the page and retry.";
        return "We couldn't save your change. Please try again shortly.";
    };

    const send = (method, body, onSuccess) => {
        busy = true;
        updateControls();
        showMessage("");
        progressWrap.hidden = false;
        progress.value = 0;
        progressLabel.textContent = method === "POST" ? "Uploading picture…" : "Removing picture…";
        if (method === "DELETE") progress.removeAttribute("value");
        const xhr = new XMLHttpRequest();
        xhr.open(method, editor.dataset.pictureEndpoint);
        xhr.withCredentials = true;
        xhr.timeout = 120000;
        xhr.setRequestHeader("RequestVerificationToken", token);
        xhr.setRequestHeader("Accept", "application/json");
        xhr.upload.addEventListener("progress", event => {
            if (!event.lengthComputable || method !== "POST") return;
            const percent = Math.round(event.loaded / event.total * 100);
            progress.value = percent;
            progressLabel.textContent = percent === 100 ? "Processing picture…" : `Uploading picture… ${percent}%`;
        });
        xhr.addEventListener("load", () => {
            if (xhr.status >= 200 && xhr.status < 300) {
                try { onSuccess(xhr); }
                catch { showMessage("Your change was saved. Reload this page to see the updated picture.", true); }
            } else showMessage(errorMessage(xhr), true);
        });
        xhr.addEventListener("error", () => showMessage("Connection lost. Check your connection and try again.", true));
        xhr.addEventListener("timeout", () => showMessage("The request took too long. Please try again.", true));
        xhr.addEventListener("loadend", () => {
            busy = false;
            progressWrap.hidden = true;
            updateControls();
            if (!selectedFile) chooseButton.focus();
        });
        xhr.send(body);
    };

    chooseButton.addEventListener("click", () => fileInput.click());
    fileInput.addEventListener("change", () => {
        const file = fileInput.files[0];
        if (!file) return;
        showMessage("");
        resetSelection();
        const extension = file.name.split(".").pop().toLowerCase();
        const expectedType = { jpg: "image/jpeg", jpeg: "image/jpeg", png: "image/png", webp: "image/webp" }[extension];
        if (!expectedType || (file.type && file.type !== expectedType)) {
            showMessage("Choose a JPEG, PNG, or WebP image with the correct file extension.", true);
            return;
        }
        if (file.size === 0 || file.size > 5 * 1024 * 1024) {
            showMessage(file.size === 0 ? "Choose an image that isn't empty." : "Choose an image no larger than 5 MB.", true);
            return;
        }
        selectedFile = file;
        previewUrl = URL.createObjectURL(file);
        image.src = previewUrl;
        image.alt = "Selected profile picture preview";
        selection.textContent = `Preview: ${file.name}. Select Upload picture to save it.`;
        updateControls();
    });
    cancelButton.addEventListener("click", () => { resetSelection(); showMessage(""); chooseButton.focus(); });
    uploadButton.addEventListener("click", () => {
        if (busy || !selectedFile) return;
        const body = new FormData();
        body.append("file", selectedFile);
        send("POST", body, xhr => {
            const response = JSON.parse(xhr.responseText);
            if (typeof response.profilePictureUrl !== "string" || !response.profilePictureUrl.startsWith("/")) {
                throw new Error("Missing picture URL");
            }
            const url = new URL(response.profilePictureUrl, window.location.origin);
            if (url.origin !== window.location.origin) throw new Error("Unexpected picture URL");
            // The API URL may already contain a version; refresh it after every successful upload.
            url.searchParams.set("v", Date.now().toString());
            savedUrl = url.href;
            hasPicture = true;
            resetSelection();
            showMessage("Your profile picture has been updated.");
            chooseButton.focus();
        });
    });
    removeButton.addEventListener("click", () => {
        if (busy || !hasPicture) return;
        send("DELETE", null, () => {
            savedUrl = editor.dataset.defaultAvatar;
            hasPicture = false;
            resetSelection();
            showMessage("Your profile picture has been removed.");
            chooseButton.focus();
        });
    });
    image.addEventListener("error", () => {
        if (selectedFile) {
            resetSelection();
            showMessage("This image could not be previewed. Choose another JPEG, PNG, or WebP image.", true);
        } else if (image.getAttribute("src") !== editor.dataset.defaultAvatar) {
            image.src = editor.dataset.defaultAvatar;
        }
    });
    window.addEventListener("pagehide", releasePreview);
    updateControls();
})();
