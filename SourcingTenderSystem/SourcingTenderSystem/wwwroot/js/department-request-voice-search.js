// Voice search for the Department My Requests title filter.
(() => {
    function bind(form) {
    const search = form?.querySelector('input[name="q"]');
    const button = form?.querySelector('[data-request-voice-search]');
    const status = form?.querySelector('[data-request-voice-status]');
    if (!search || !button || !status || button.dataset.bound) return;
    button.dataset.bound = 'true';
    button.hidden = false;
    const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!Recognition) {
        button.disabled = true;
        button.title = window.stmsT('Voice search is not supported in this browser');
        status.textContent = button.title;
        return;
    }
    const recognition = new Recognition();
    recognition.lang = document.documentElement.lang || 'en-US';
    recognition.interimResults = false;
    recognition.maxAlternatives = 1;
    recognition.onstart = () => {
        button.disabled = true;
        status.textContent = window.stmsT('Listening for a request title');
    };
    recognition.onresult = event => {
        search.value = event.results[0][0].transcript;
        status.textContent = window.stmsFormat("Searching for {0}", search.value);
        if (form.isConnected) form.requestSubmit();
    };
    recognition.onerror = event => {
        status.textContent = event.error === 'not-allowed'
            ? window.stmsT('Microphone access was denied. Type a title to search.')
            : window.stmsT('Voice search failed. Try again or type a title.');
    };
    recognition.onend = () => { button.disabled = false; };
    button.addEventListener('click', () => {
        try { recognition.start(); }
        catch { status.textContent = window.stmsT('Could not start voice search.'); }
    });
    }
    const init = () => document.querySelectorAll('.request-filters').forEach(bind);
    document.addEventListener('workspace:load', init);
    init();
})();
