// Voice search for the Department My Requests title filter.
(() => {
    const form = document.querySelector('.request-filters');
    const search = form?.querySelector('input[name="q"]');
    const button = form?.querySelector('[data-request-voice-search]');
    const status = form?.querySelector('[data-request-voice-status]');
    if (!search || !button || !status) return;
    const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!Recognition) {
        button.disabled = true;
        button.title = 'Voice search is not supported in this browser';
        status.textContent = button.title;
        return;
    }
    const recognition = new Recognition();
    recognition.lang = document.documentElement.lang || 'en-US';
    recognition.interimResults = false;
    recognition.maxAlternatives = 1;
    recognition.onstart = () => {
        button.disabled = true;
        status.textContent = 'Listening for a request title';
    };
    recognition.onresult = event => {
        search.value = event.results[0][0].transcript;
        status.textContent = 'Searching for ' + search.value;
        form.requestSubmit();
    };
    recognition.onerror = event => {
        status.textContent = event.error === 'not-allowed'
            ? 'Microphone access was denied. Type a title to search.'
            : 'Voice search failed. Try again or type a title.';
    };
    recognition.onend = () => { button.disabled = false; };
    button.addEventListener('click', () => {
        try { recognition.start(); }
        catch { status.textContent = 'Could not start voice search.'; }
    });
})();
