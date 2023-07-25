// Custom function to send event data to Google Analytics
function sendCustomEvent(eventName, eventCategory, eventAction, eventLabel, eventValue) {
    gtag('event', eventName, {
        'event_category': eventCategory,
        'event_action': eventAction,
        'event_label': eventLabel,
        'event_value': eventValue
    });
}
