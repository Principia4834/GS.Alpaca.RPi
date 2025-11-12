/*
  webview_shim.c
  Native shim to create a WebKitGTK web view and expose a simple C API for embedding
  into an existing X11 window using XReparentWindow.

  Notes:
  - Targets X11 (uses X11 reparenting). Wayland is not supported by this shim.
  - Requires GTK3 and WebKit2GTK to be installed.
  - Build with the provided Makefile:
      make
    That will produce libwebview_shim.so

  API:
    // Returns an opaque pointer (handle) to the created webview instance.
    void* create_webview(void* parent_xid, int x, int y, int width, int height);

    // Destroy the created webview instance and free resources.
    void destroy_webview(void* handle);

    // Navigate to URL (utf8)
    void navigate_webview(void* handle, const char* url);

    // Resize the child webview
    void resize_webview(void* handle, int x, int y, int width, int height);

    // Execute JS (fire-and-forget)
    void execute_js(void* handle, const char* script);

    // Navigation helpers (new):
    // int can_go_back(void* handle);    // returns 1 if can go back, 0 otherwise
    // int can_go_forward(void* handle); // returns 1 if can go forward, 0 otherwise
    // void go_back(void* handle);
    // void go_forward(void* handle);
    // void reload_webview(void* handle);

  Limitations:
  - Minimal error checking to keep example short.
  - Production code should add more robust synchronization and error paths.
*/

#define _GNU_SOURCE
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <pthread.h>
#include <stdint.h>

#include <gtk/gtk.h>
#include <webkit2/webkit2.h>
#include <gdk/gdkx.h> // for GDK X11 helpers
#include <X11/Xlib.h>

typedef struct {
    GtkWidget *web_view;
    GtkWidget *container; // a GtkWindow used as a holder (we will reparent its XID)
    GdkWindow *gdk_window;
    Window xid; // X11 window id
} WebViewInstance;

static pthread_t gtk_thread;
static int gtk_thread_started = 0;
static GMainContext *gtk_context = NULL;
static GMainLoop *gtk_loop = NULL;
static GMutex instance_lock;
static GCond instance_cond;

/* Simple queue of functions to run on GTK main loop */
typedef struct Task {
    void (*func)(void*);
    void *arg;
    struct Task *next;
} Task;

static Task *task_head = NULL;
static Task *task_tail = NULL;

static void push_task(void (*func)(void*), void *arg) {
    Task *t = g_new0(Task, 1);
    t->func = func;
    t->arg = arg;
    t->next = NULL;

    g_mutex_lock(&instance_lock);
    if (task_tail) task_tail->next = t;
    else task_head = t;
    task_tail = t;
    g_cond_signal(&instance_cond);
    g_mutex_unlock(&instance_lock);
}

/* Called on GTK thread to pop and run tasks */
static gboolean pump_tasks(gpointer _) {
    Task *t = NULL;
    g_mutex_lock(&instance_lock);
    if (task_head) {
        t = task_head;
        task_head = task_head->next;
        if (!task_head) task_tail = NULL;
    }
    g_mutex_unlock(&instance_lock);

    if (t) {
        if (t->func) t->func(t->arg);
        g_free(t);
    }

    return G_SOURCE_CONTINUE;
}

/* Initialize GTK on a dedicated thread */
static void* gtk_thread_func(void *arg) {
    /* Initialize GTK on this thread */
    gtk_init(NULL, NULL);

    /* Create a private main context and loop so we don't interfere with other code */
    gtk_context = g_main_context_new();
    g_main_context_push_thread_default(gtk_context);
    gtk_loop = g_main_loop_new(gtk_context, FALSE);

    /* Add a recurring idle source to process queued tasks */
    g_idle_add_full(G_PRIORITY_DEFAULT, pump_tasks, NULL, NULL);

    g_mutex_lock(&instance_lock);
    gtk_thread_started = 1;
    g_cond_signal(&instance_cond);
    g_mutex_unlock(&instance_lock);

    /* Run the GTK loop */
    g_main_loop_run(gtk_loop);

    /* Cleanup on exit */
    g_main_context_pop_thread_default(gtk_context);
    g_main_context_unref(gtk_context);
    gtk_context = NULL;
    g_main_loop_unref(gtk_loop);
    gtk_loop = NULL;
    return NULL;
}

/* Ensure the GTK thread is started */
static void ensure_gtk_thread() {
    g_mutex_lock(&instance_lock);
    if (!gtk_thread_started) {
        g_mutex_unlock(&instance_lock);
        g_mutex_init(&instance_lock);
        g_cond_init(&instance_cond);

        if (pthread_create(&gtk_thread, NULL, gtk_thread_func, NULL) != 0) {
            fprintf(stderr, "webview_shim: failed to create GTK thread\n");
            return;
        }

        /* Wait until gtk thread reports started */
        g_mutex_lock(&instance_lock);
        while (!gtk_thread_started) {
            g_cond_wait(&instance_cond, &instance_lock);
        }
        g_mutex_unlock(&instance_lock);
    } else {
        g_mutex_unlock(&instance_lock);
    }
}

/* Utility: synchronously run a function on GTK thread (blocks until done) */
typedef struct SyncCall {
    void (*func)(void*);
    void *arg;
    GMutex m;
    GCond c;
    int done;
} SyncCall;

static void sync_call_trampoline(void *data) {
    SyncCall *sc = (SyncCall*)data;
    sc->func(sc->arg);
    g_mutex_lock(&sc->m);
    sc->done = 1;
    g_cond_signal(&sc->c);
    g_mutex_unlock(&sc->m);
}

static void run_on_gtk_thread_sync(void (*func)(void*), void *arg) {
    SyncCall sc;
    sc.func = func;
    sc.arg = arg;
    sc.done = 0;
    g_mutex_init(&sc.m);
    g_cond_init(&sc.c);
    push_task(sync_call_trampoline, &sc);
    g_mutex_lock(&sc.m);
    while (!sc.done) g_cond_wait(&sc.c, &sc.m);
    g_mutex_unlock(&sc.m);
    g_mutex_clear(&sc.m);
}

/* Implementation helpers */

static void create_instance_on_ui_thread(void *arg) {
    WebViewInstance *inst = (WebViewInstance*)arg;

    /* Create a top-level window to host the WebView widget. We will reparent the
       window to the provided parent XID so the final native window is embedded. */
    inst->container = gtk_window_new(GTK_WINDOW_TOPLEVEL);
    gtk_widget_set_size_request(inst->container, 100, 100);

    inst->web_view = webkit_web_view_new();
    gtk_container_add(GTK_CONTAINER(inst->container), inst->web_view);
    gtk_widget_show_all(inst->container);

    /* Realize so GdkWindow is created */
    GdkWindow *gdkwin = gtk_widget_get_window(inst->container);
    inst->gdk_window = gdkwin;

    /* Obtain X11 display and XID */
    Display *disp = GDK_DISPLAY_XDISPLAY(gdk_window_get_display(gdkwin));
    Window xid = GDK_WINDOW_XID(gdkwin);
    inst->xid = xid;

    /* If parent was supplied, reparent the top-level container window under the parent XID */
    if (inst->xid && inst->xid != 0) {
        /* nothing here; parent-based reparent is done in create_webview wrapper */
    }
}

static void destroy_instance_on_ui_thread(void *arg) {
    WebViewInstance *inst = (WebViewInstance*)arg;
    if (!inst) return;
    if (inst->container) {
        gtk_widget_hide(inst->container);
        gtk_widget_destroy(inst->container);
        inst->container = NULL;
    }
    inst->web_view = NULL;
}

/* Trampolines for synchronous boolean queries */
typedef struct BoolCall {
    WebViewInstance *inst;
    gboolean result;
} BoolCall;

static void can_go_back_call(void *arg) {
    BoolCall *bc = (BoolCall*)arg;
    if (bc->inst && bc->inst->web_view) {
        bc->result = webkit_web_view_can_go_back(WEBKIT_WEB_VIEW(bc->inst->web_view));
    } else {
        bc->result = FALSE;
    }
}

static void can_go_forward_call(void *arg) {
    BoolCall *bc = (BoolCall*)arg;
    if (bc->inst && bc->inst->web_view) {
        bc->result = webkit_web_view_can_go_forward(WEBKIT_WEB_VIEW(bc->inst->web_view));
    } else {
        bc->result = FALSE;
    }
}

/* Fire-and-forget actions */
static void go_back_action(void *arg) {
    WebViewInstance *inst = (WebViewInstance*)arg;
    if (inst && inst->web_view) {
        webkit_web_view_go_back(WEBKIT_WEB_VIEW(inst->web_view));
    }
}
static void go_forward_action(void *arg) {
    WebViewInstance *inst = (WebViewInstance*)arg;
    if (inst && inst->web_view) {
        webkit_web_view_go_forward(WEBKIT_WEB_VIEW(inst->web_view));
    }
}
static void reload_action(void *arg) {
    WebViewInstance *inst = (WebViewInstance*)arg;
    if (inst && inst->web_view) {
        webkit_web_view_reload(WEBKIT_WEB_VIEW(inst->web_view));
    }
}

/* API exported to .NET */

void* create_webview(void* parent_xid_ptr, int x, int y, int width, int height) {
    ensure_gtk_thread();

    WebViewInstance *inst = g_new0(WebViewInstance, 1);

    /* If parent_xid_ptr is provided, treat as Window (XID) */
    Window parent_xid = 0;
    if (parent_xid_ptr != NULL) {
        parent_xid = (Window)(uintptr_t)parent_xid_ptr;
    }

    /* Create widget on GTK thread synchronously */
    run_on_gtk_thread_sync(create_instance_on_ui_thread, inst);

    /* After instance is created, reparent its window under parent_xid (if provided) */
    if (parent_xid != 0 && inst->gdk_window != NULL) {
        Display *disp = GDK_DISPLAY_XDISPLAY(gdk_window_get_display(inst->gdk_window));
        Window child_xid = GDK_WINDOW_XID(inst->gdk_window);
        if (disp != NULL) {
            /* Reparent using XReparentWindow */
            XReparentWindow(disp, child_xid, parent_xid, x, y);
            XFlush(disp);
        }
    } else {
        /* If no parent provided, we just position the created toplevel */
        if (inst->container) {
            gtk_window_move(GTK_WINDOW(inst->container), x, y);
            gtk_window_set_default_size(GTK_WINDOW(inst->container), width, height);
        }
    }

    /* Return the pointer to WebViewInstance as opaque handle */
    return (void*)inst;
}

void destroy_webview(void* handle) {
    if (!handle) return;
    WebViewInstance *inst = (WebViewInstance*)handle;
    run_on_gtk_thread_sync(destroy_instance_on_ui_thread, inst);
    g_free(inst);
}

void navigate_webview(void* handle, const char* url) {
    if (!handle || !url) return;
    WebViewInstance *inst = (WebViewInstance*)handle;
    /* Capture url */
    char *cpy = g_strdup(url);
    void nav(void *arg) {
        WebViewInstance *i = (WebViewInstance*)arg;
        if (i->web_view) {
            webkit_web_view_load_uri(WEBKIT_WEB_VIEW(i->web_view), cpy);
        }
        g_free(cpy);
    }
    push_task(nav, inst);
}

void resize_webview(void* handle, int x, int y, int width, int height) {
    if (!handle) return;
    WebViewInstance *inst = (WebViewInstance*)handle;
    void rs(void *arg) {
        WebViewInstance *i = (WebViewInstance*)arg;
        if (i->container) {
            gtk_window_move(GTK_WINDOW(i->container), x, y);
            gtk_window_resize(GTK_WINDOW(i->container), width, height);
        }
        else if (i->web_view) {
            gtk_widget_set_size_request(i->web_view, width, height);
        }
    }
    push_task(rs, inst);
}

void execute_js(void* handle, const char* script) {
    if (!handle || !script) return;
    WebViewInstance *inst = (WebViewInstance*)handle;
    char *cpy = g_strdup(script);
    void ej(void *arg) {
        WebViewInstance *i = (WebViewInstance*)arg;
        if (i->web_view) {
            webkit_web_view_run_javascript(WEBKIT_WEB_VIEW(i->web_view), cpy, NULL, NULL, NULL);
        }
        g_free(cpy);
    }
    push_task(ej, inst);
}

/* New navigation helpers */

int can_go_back(void* handle) {
    if (!handle) return 0;
    WebViewInstance *inst = (WebViewInstance*)handle;
    BoolCall bc;
    bc.inst = inst;
    bc.result = FALSE;
    run_on_gtk_thread_sync(can_go_back_call, &bc);
    return bc.result ? 1 : 0;
}

int can_go_forward(void* handle) {
    if (!handle) return 0;
    WebViewInstance *inst = (WebViewInstance*)handle;
    BoolCall bc;
    bc.inst = inst;
    bc.result = FALSE;
    run_on_gtk_thread_sync(can_go_forward_call, &bc);
    return bc.result ? 1 : 0;
}

void go_back(void* handle) {
    if (!handle) return;
    WebViewInstance *inst = (WebViewInstance*)handle;
    push_task(go_back_action, inst);
}

void go_forward(void* handle) {
    if (!handle) return;
    WebViewInstance *inst = (WebViewInstance*)handle;
    push_task(go_forward_action, inst);
}

void reload_webview(void* handle) {
    if (!handle) return;
    WebViewInstance *inst = (WebViewInstance*)handle;
    push_task(reload_action, inst);
}