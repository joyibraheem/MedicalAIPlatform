// GLOBAL LANGUAGE SYSTEM
// Application-level translation engine

(function () {
    const body = document.body;
    const html = document.documentElement;

    // Use global I18N dictionary if available, otherwise fallback to local
    const translations = window.I18N || {
        en: {
            brand_title: "MedicalAIPlatform",
            brand_title_footer: "MedicalAIPlatform",
            nav_home: "Home",
            nav_privacy: "Privacy",
            nav_settings: "Settings",
            nav_admin: "Admin",
            nav_login: "Login",
            nav_register: "Register",
            nav_logout: "Logout",
            nav_hello: "Hello",
            settings_title: "User Settings",
            settings_darkmode_label: "Enable dark mode",
            settings_language_label: "Language",
            settings_auto_save_hint: "Changes are applied automatically.",
            home_title: "Multimodal AI Platform for Medical Diagnostics",
            home_intro: "Secure, AI-assisted diagnostic tools for medical professionals. Please log in or register as a doctor to access the platform.",
            home_register_doctor: "Register as Doctor",
            home_admin_title: "Admin Dashboard",
            home_admin_intro: "Admin dashboard placeholder: manage doctors, review system activity, and configure AI models.",
            home_doctor_title: "Doctor Workspace",
            home_doctor_intro: "Welcome! This is your doctor dashboard placeholder for managing patients and diagnostic analyses.",
            home_no_role: "You are logged in, but no specific role-based dashboard is configured for your account.",
            // Calendar translations
            calendar_schedule: "Schedule",
            calendar_upcoming: "Upcoming",
            calendar_new_appointment: "+ New Appointment",
            calendar_no_appointments: "No upcoming appointments",
            day_sun: "Sun",
            day_mon: "Mon",
            day_tue: "Tue",
            day_wed: "Wed",
            day_thu: "Thu",
            day_fri: "Fri",
            day_sat: "Sat",
            appointment_checkup: "Checkup",
            appointment_follow_up: "Follow-up",
            appointment_emergency: "Emergency",
            appointment_consultation: "Consultation",
            // FollowUp translations
            followup_title: "Patient Follow-Up & Monitoring",
            followup_subtitle: "Manage post-scan actions and AI-driven recommendations",
            followup_compliance_rate: "Compliance Rate",
            followup_high_urgency: "High Urgency cases",
            followup_immediate_referral: "Requiring immediate referral",
            followup_scheduled_today: "Scheduled Today",
            followup_pid: "PID:",
            urgency_high: "High",
            urgency_medium: "Medium",
            urgency_low: "Low"
        },
        ar: {
            brand_title: "منصة الذكاء الطبي",
            brand_title_footer: "منصة الذكاء الطبي",
            nav_home: "الصفحة الرئيسية",
            nav_privacy: "سياسة الخصوصية",
            nav_settings: "الإعدادات",
            nav_admin: "لوحة المشرف",
            nav_login: "تسجيل الدخول",
            nav_register: "إنشاء حساب",
            nav_logout: "تسجيل الخروج",
            nav_hello: "مرحباً",
            settings_title: "إعدادات المستخدم",
            settings_darkmode_label: "تفعيل الوضع الداكن",
            settings_language_label: "اللغة",
            settings_auto_save_hint: "يتم تطبيق التغييرات تلقائياً.",
            home_title: "منصة متعددة الوسائط للذكاء الاصطناعي للتشخيص الطبي",
            home_intro: "أدوات تشخيص طبية مدعومة بالذكاء الاصطناعي للأطباء. يرجى تسجيل الدخول أو إنشاء حساب للوصول إلى المنصة.",
            home_register_doctor: "تسجيل كطبيب",
            home_admin_title: "لوحة إدارة النظام",
            home_admin_intro: "من هنا يمكن للمشرف إدارة الأطباء ومراجعة نشاط النظام وضبط إعدادات النماذج.",
            home_doctor_title: "مساحة عمل الطبيب",
            home_doctor_intro: "مرحباً! هذه مساحة عملك لإدارة المرضى والتحليلات التشخيصية.",
            home_no_role: "أنت مسجل الدخول، ولكن لا يوجد لوح تحكم مخصص لدورك حتى الآن.",
            // Calendar translations
            calendar_schedule: "الجدول",
            calendar_upcoming: "القادم",
            calendar_new_appointment: "+ موعد جديد",
            calendar_no_appointments: "لا توجد مواعيد قادمة",
            day_sun: "أحد",
            day_mon: "اثنين",
            day_tue: "ثلاثاء",
            day_wed: "أربعاء",
            day_thu: "خميس",
            day_fri: "جمعة",
            day_sat: "سبت",
            appointment_checkup: "فحص",
            appointment_follow_up: "متابعة",
            appointment_emergency: "طوارئ",
            appointment_consultation: "استشارة",
            // FollowUp translations
            followup_title: "متابعة ومراقبة المرضى",
            followup_subtitle: "إدارة الإجراءات بعد الفحص والتوصيات المدعومة بالذكاء الاصطناعي",
            followup_compliance_rate: "معدل الامتثال",
            followup_high_urgency: "حالات عالية الأولوية",
            followup_immediate_referral: "تتطلب إحالة فورية",
            followup_scheduled_today: "المجدولة اليوم",
            followup_pid: "رقم المريض:",
            urgency_high: "عالية",
            urgency_medium: "متوسطة",
            urgency_low: "منخفضة"
        }
    };
    
    // Make translations globally accessible
    window.calendarTranslations = translations;
    if (!window.I18N) {
        window.I18N = translations;
    }

    // GLOBAL LANGUAGE RESOLUTION - SINGLE SOURCE OF TRUTH
    function getCurrentLanguage() {
        // Use localStorage.getItem("lang") as per requirement
        const lang = localStorage.getItem("lang");
        return (lang === "ar") ? "ar" : "en";
    }

    // GLOBAL TRANSLATION ENGINE
    function setLanguage(lang) {
        // Use global I18N dictionary (from i18n.js)
        const dict = window.I18N || translations;
        if (!dict || !dict[lang]) {
            lang = "en";
        }
        const translationsDict = dict[lang];

        // Translate ALL elements with data-i18n attribute
        document.querySelectorAll("[data-i18n]").forEach(el => {
            const key = el.getAttribute("data-i18n");
            if (!key) return;

            const text = translationsDict[key];
            if (!text) {
                console.warn(`Missing translation key: ${key} for language: ${lang}`);
                return;
            }

            // Handle different element types
            if (el.tagName.toLowerCase() === "input") {
                if (el.type === "button" || el.type === "submit") {
                    el.value = text;
                } else if (el.type === "text" || el.type === "email" || el.type === "password") {
                    el.placeholder = text;
                }
            } else if (el.tagName.toLowerCase() === "textarea") {
                el.placeholder = text;
            } else {
                // Handle special cases with dynamic content
                if (key === "nav_hello") {
                    const current = el.textContent;
                    const namePart = current.replace(/^[^،:,]+[،:,]?\s*/u, "");
                    el.textContent = text + (namePart ? "، " + namePart : "");
                } else {
                    // Check if element has a <span> child (for buttons with icons)
                    const span = el.querySelector("span");
                    if (span) {
                        span.textContent = text;
                    } else {
                        el.textContent = text;
                    }
                }
            }
        });

        // Update HTML lang and dir attributes - LANGUAGE-BOUND ONLY
        html.setAttribute("lang", lang);
        html.setAttribute("dir", lang === "ar" ? "rtl" : "ltr");
        
        // Save to localStorage using "lang" key as per requirement
        localStorage.setItem("lang", lang);
        
        // Also update medicalAiPrefs for backward compatibility
        const prefs = loadPreferencesLocal();
        savePreferencesLocal(prefs?.isDarkMode || false, lang);
        
        // Trigger custom event for calendar/FollowUp scripts
        document.dispatchEvent(new CustomEvent('languageChanged', { detail: { lang } }));
    }

    // Expose globally
    window.setLanguage = setLanguage;

    function setTheme(isDark) {
        // Apply theme globally to HTML element
        document.documentElement.setAttribute("data-theme", isDark ? "dark" : "light");
        document.documentElement.classList.toggle("light-mode-global", !isDark);
        
        // Apply to body element
        if (document.body) {
            document.body.classList.toggle("light-mode", !isDark);
            document.body.classList.toggle("dark-mode", isDark);
        }
        
        // Save to localStorage
        localStorage.setItem("theme", isDark ? "dark" : "light");
        
        // Update preferences
        const prefs = loadPreferencesLocal();
        if (prefs) {
            savePreferencesLocal(isDark, prefs.language || "en");
        } else {
            savePreferencesLocal(isDark, "en");
        }
        
        // Trigger custom event for components that need to react to theme changes
        document.dispatchEvent(new CustomEvent('themeChanged', { detail: { isDark } }));
    }
    
    // Expose globally
    window.setTheme = setTheme;

    function savePreferencesLocal(isDark, lang) {
        try {
            const prefs = { isDarkMode: !!isDark, language: lang };
            localStorage.setItem("medicalAiPrefs", JSON.stringify(prefs));
        } catch { /* ignore */ }
    }

    function loadPreferencesLocal() {
        try {
            const raw = localStorage.getItem("medicalAiPrefs");
            if (!raw) return null;
            return JSON.parse(raw);
        } catch {
            return null;
        }
    }

    function getAntiForgeryToken() {
        return (window.SettingsPage && window.SettingsPage.antiForgeryToken) ||
            document.querySelector('input[name="__RequestVerificationToken"]')?.value;
    }

    async function postSettings(isDark, lang) {
        const token = getAntiForgeryToken();
        try {
            await fetch("/Settings/UpdateSettings", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "RequestVerificationToken": token || ""
                },
                body: JSON.stringify({ isDarkMode: !!isDark, language: lang })
            });
        } catch {
            // ignore errors – client still updates UI and local storage
        }
    }

    function initSettingsPage() {
        const darkToggle = document.getElementById("settings-darkmode-toggle");
        const langSelect = document.getElementById("settings-language-select");

        if (!darkToggle && !langSelect) return;

        const prefs = loadPreferencesLocal();
        if (prefs) {
            if (typeof prefs.isDarkMode === "boolean") {
                darkToggle && (darkToggle.checked = prefs.isDarkMode);
                // Theme already set by inline script in <head> - do NOT re-apply
            }
            if (prefs.language) {
                langSelect && (langSelect.value = prefs.language);
                setLanguage(prefs.language);
            }
        }

        if (darkToggle) {
            darkToggle.addEventListener("change", () => {
                const isDark = darkToggle.checked;
                const lang = (langSelect && langSelect.value) || "en";
                setTheme(isDark);
                savePreferencesLocal(isDark, lang);
                postSettings(isDark, lang);
            });
        }

        if (langSelect) {
            langSelect.addEventListener("change", () => {
                const lang = langSelect.value || "en";
                const isDark = !!(darkToggle && darkToggle.checked);
                setLanguage(lang);
                savePreferencesLocal(isDark, lang);
                postSettings(isDark, lang);
            });
        }
    }

    // GLOBAL LANGUAGE INITIALIZATION
    // Apply language BEFORE DOMContentLoaded to avoid flash
    (function initLanguageImmediate() {
        const lang = getCurrentLanguage();
        setLanguage(lang);
    })();

    document.addEventListener("DOMContentLoaded", () => {
        // Initialize theme from localStorage or server
        const savedTheme = localStorage.getItem("theme");
        const prefs = loadPreferencesLocal();
        
        // Determine dark mode state
        let isDarkMode = true; // Default to dark
        if (savedTheme === "light" || savedTheme === "dark") {
            isDarkMode = savedTheme === "dark";
        } else if (prefs && typeof prefs.isDarkMode === "boolean") {
            isDarkMode = prefs.isDarkMode;
        }
        
        // Apply theme globally
        setTheme(isDarkMode);
        
        // Re-apply translations in case DOM wasn't ready
        const lang = getCurrentLanguage();
        setLanguage(lang);
        
        // Save English as default if no preference exists
        if (!prefs || !prefs.language) {
            savePreferencesLocal(isDarkMode, "en");
        }

        initSettingsPage();
    });
})();