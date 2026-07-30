# Makefile for autoclicker shared library
# Builds libautoclicker.so for Linux

CXX      := g++
CXXFLAGS := -fPIC -O2 -Wall -Wextra
LDFLAGS  := -shared
LIBS     := -lX11 -lXtst -lpthread
TARGET   := libautoclicker.so

SRCS     := autoclicker.cpp
OBJS     := $(SRCS:.cpp=.o)

.PHONY: all clean

all: $(TARGET)

$(TARGET): $(OBJS)
	$(CXX) $(CXXFLAGS) $(LDFLAGS) -o $@ $^ $(LIBS)
	@echo "✓ Built $(TARGET)"

%.o: %.cpp autoclicker.h
	$(CXX) $(CXXFLAGS) -c $< -o $@

clean:
	rm -f $(OBJS) $(TARGET)
	@echo "✓ Cleaned"
