// Shared voice search for Admin Employees, Suppliers, and Departments.
(() => {
    const directory = document.querySelector('[data-directory]');
    const search = directory?.querySelector('[data-filter-search]');
    const button = directory?.querySelector('[data-directory-voice-search]');
    if (!search || !button) return;
    const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!Recognition) {
        button.disabled = true;
        button.title = 'Voice search is not supported in this browser';
        return;
    }
    const recognition = new Recognition();
    recognition.lang = document.documentElement.lang || 'en-US';
    recognition.interimResults = false;
    recognition.maxAlternatives = 1;
    recognition.onstart = () => {
        button.disabled = true;
        button.setAttribute('aria-label', 'Listening for search terms');
    };
    recognition.onresult = event => {
        search.value = event.results[0][0].transcript;
        search.dispatchEvent(new Event('input', { bubbles: true }));
        search.focus();
    };
    recognition.onerror = event => {
        button.title = event.error === 'not-allowed'
            ? 'Microphone access was denied'
            : 'Voice search failed. Please try again';
    };
    recognition.onend = () => {
        button.disabled = false;
        button.setAttribute('aria-label', 'Search by voice');
    };
    button.addEventListener('click', () => {
        try { recognition.start(); }
        catch { button.title = 'Could not start voice search'; }
    });
})();
