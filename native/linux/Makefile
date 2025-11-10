# Build the Linux native shim (X11 + GTK3 + WebKit2)
PKG := $(shell pkg-config --cflags --libs gtk+-3.0 webkit2gtk-4.0)
CFLAGS := -fPIC -O2 -Wall

all: libwebview_shim.so

libwebview_shim.so: webview_shim.c
	$(CC) $(CFLAGS) -shared webview_shim.c -o libwebview_shim.so $(PKG) -lX11

clean:
	rm -f libwebview_shim.so
